using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PoRedoImage.Application.Configuration;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Shared.Configuration;

namespace PoRedoImage.Infrastructure.Services;

/// <summary>
/// Google Veo 3.1 Lite implementation of <see cref="IVideoGenerationService"/>.
/// Adapter pattern (GoF): wraps the Gemini <c>v1beta/models/{model}:predictLongRunning</c> endpoint.
/// </summary>
/// <remarks>
/// <para>
/// Shares the <c>GeminiApi</c> named client with <see cref="GeminiImagen3Service"/> and
/// <see cref="LyriaMusicService"/>: same host, same <c>Google:ApiKey</c>, so it inherits the
/// standard resilience pipeline, the <c>MockAiDelegatingHandler</c> budget guardrail, and outbound
/// correlation headers.
/// </para>
/// <para>
/// Lite at 720p is the deliberate default. Veo is metered per second of output, and the tiers are
/// far apart: Lite is $0.05/s against Standard's $0.40/s, so one 8-second clip costs $0.40 instead
/// of $3.20 — an 8× difference on a feature a user can trigger repeatedly. Raising the tier is a
/// pricing decision, not a quality tweak; change <c>Google:VeoModel</c> deliberately and update
/// <c>AiPricingOptions</c> in the same change so the cost meter cannot disagree with the bill.
/// </para>
/// <para>
/// The download step is a second hop: the finished operation carries a URI, not the bytes, and that
/// URI needs the API key and redirect-following to resolve.
/// </para>
/// </remarks>
public sealed class VeoVideoGenerationService : IVideoGenerationService
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta";
    private const string DefaultModel = "veo-3.1-lite-generate-preview";
    private const string DefaultResolution = "720p";

    /// <summary>
    /// Every clip is 8 seconds — Veo's maximum, and the only length this app offers. Veo also
    /// accepts 4 and 6; they are not exposed because a shorter clip is not cheaper per render in
    /// any way the user would notice choosing, and one fixed length keeps the per-render cost a
    /// single known number ($0.40 at Lite/720p).
    /// </summary>
    public const int ClipSeconds = 8;

    private readonly ILogger<VeoVideoGenerationService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly string _model;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_configuration[ConfigKeys.GoogleApiKey]);

    public VeoVideoGenerationService(
        IConfiguration configuration, IHttpClientFactory httpClientFactory, ILogger<VeoVideoGenerationService> logger)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _model = configuration[ConfigKeys.GoogleVeoModel] ?? DefaultModel;

        // Same defense-in-depth budget guardrail as GeminiImagen3Service and LyriaMusicService, and
        // it matters more here: video is the most expensive call in the app by an order of magnitude.
        if (ConfigValue.Bool(configuration, ConfigKeys.MocksUseMockAi))
        {
            throw new InvalidOperationException(
                "VeoVideoGenerationService was constructed while Mocks:UseMockAi=true. The DI "
                + "container should have resolved MockVeoVideoGenerationService instead. Blocking "
                + "construction to guarantee zero live token spend in test/dev paths.");
        }

        if (IsConfigured)
            _logger.LogInformation("Veo video service initialized. Model={Model}", _model);
        else
            _logger.LogInformation("Google:ApiKey not configured; video generation is disabled.");
    }

    public async Task<string> StartAsync(
        byte[] image, string contentType, string prompt, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException(
                "Video generation is not configured. Set Google:ApiKey via Key Vault.");

        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var body = new
        {
            instances = new[]
            {
                new
                {
                    prompt,
                    image = new
                    {
                        inlineData = new
                        {
                            mimeType = contentType,
                            data = Convert.ToBase64String(image),
                        },
                    },
                },
            },
            parameters = new
            {
                durationSeconds = ClipSeconds,
                resolution = DefaultResolution,
                sampleCount = 1,
            },
        };

        var client = _httpClientFactory.CreateClient("GeminiApi");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/models/{_model}:predictLongRunning");
        // Re-read the key each call so a rotated Key Vault secret is picked up by this singleton.
        request.Headers.Add("x-goog-api-key", _configuration[ConfigKeys.GoogleApiKey] ?? string.Empty);
        // IL2026: the outbound body is an anonymous type shaped to the third-party API's exact
        // contract, and System.Text.Json source generation cannot describe anonymous types. This
        // assembly is server-side only and is never trimmed, so the reflective writer is safe here.
        #pragma warning disable IL2026
        request.Content = JsonContent.Create(body);
        #pragma warning restore IL2026

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Veo start error {Status}: {Body}", (int)response.StatusCode, errorBody);
            throw new InvalidOperationException($"Veo API returned {(int)response.StatusCode}: {errorBody}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!json.RootElement.TryGetProperty("name", out var nameEl) || nameEl.GetString() is not { } operationName)
            throw new InvalidOperationException("Veo did not return an operation name.");

        _logger.LogInformation(
            "Veo job started. Operation={Operation}, Duration={Duration}s", operationName, ClipSeconds);
        return operationName;
    }

    public async Task<VideoGenerationStatus> PollAsync(string operationName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);

        var client = _httpClientFactory.CreateClient("GeminiApi");
        var apiKey = _configuration[ConfigKeys.GoogleApiKey] ?? string.Empty;

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/{operationName}");
        request.Headers.Add("x-goog-api-key", apiKey);

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Veo poll error {Status}: {Body}", (int)response.StatusCode, errorBody);
            return VideoGenerationStatus.Failed($"Video service returned {(int)response.StatusCode}.");
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = json.RootElement;

        if (!root.TryGetProperty("done", out var doneEl) || !doneEl.GetBoolean())
            return VideoGenerationStatus.Pending();

        // A finished operation carrying an "error" is the provider reporting a refusal or fault.
        // Surface the message rather than a generic failure — for video the usual cause is the
        // safety filter reacting to the source photo, which the user can act on only if told.
        if (root.TryGetProperty("error", out var errEl))
        {
            var message = errEl.TryGetProperty("message", out var msgEl)
                ? msgEl.GetString() ?? "Video generation was rejected."
                : "Video generation was rejected.";
            _logger.LogWarning("Veo operation {Operation} finished with an error: {Message}", operationName, message);
            return VideoGenerationStatus.Failed(message);
        }

        if (!TryExtractVideoUri(root, out var videoUri))
        {
            _logger.LogWarning("Veo operation {Operation} completed with no video payload.", operationName);
            return VideoGenerationStatus.Failed(
                "The video service finished without returning a clip. This usually means the "
                + "prompt or photo was filtered — try a different photo or a milder prompt.");
        }

        // Second hop: the operation gives a URI, not bytes. It needs the API key, and it redirects.
        using var download = new HttpRequestMessage(HttpMethod.Get, videoUri);
        download.Headers.Add("x-goog-api-key", apiKey);
        using var videoResponse = await client.SendAsync(download, ct);

        if (!videoResponse.IsSuccessStatusCode)
        {
            _logger.LogError("Veo download failed with {Status}", (int)videoResponse.StatusCode);
            return VideoGenerationStatus.Failed("The finished video could not be downloaded.");
        }

        var bytes = await videoResponse.Content.ReadAsByteArrayAsync(ct);
        var mediaType = videoResponse.Content.Headers.ContentType?.MediaType ?? "video/mp4";

        _logger.LogInformation(
            "Veo operation {Operation} complete. {Bytes} bytes, {Type}.", operationName, bytes.Length, mediaType);
        return new VideoGenerationStatus(Done: true, Video: bytes, ContentType: mediaType);
    }

    /// <summary>
    /// Digs the video URI out of the completed operation. The response nests it under
    /// <c>response.generateVideoResponse.generatedSamples[].video.uri</c>, with older shapes using
    /// <c>generatedVideos[]</c>; both are accepted so a response-shape change does not read to the
    /// user as a silent "no video was produced".
    /// </summary>
    private static bool TryExtractVideoUri(JsonElement root, out string uri)
    {
        uri = string.Empty;
        if (!root.TryGetProperty("response", out var response)) return false;

        foreach (var wrapper in new[] { "generateVideoResponse", "generateVideosResponse" })
        {
            if (!response.TryGetProperty(wrapper, out var inner)) continue;

            foreach (var collection in new[] { "generatedSamples", "generatedVideos" })
            {
                if (!inner.TryGetProperty(collection, out var samples)
                    || samples.ValueKind != JsonValueKind.Array) continue;

                foreach (var sample in samples.EnumerateArray())
                {
                    if (sample.TryGetProperty("video", out var video)
                        && video.TryGetProperty("uri", out var uriEl)
                        && uriEl.GetString() is { Length: > 0 } found)
                    {
                        uri = found;
                        return true;
                    }
                }
            }
        }

        return false;
    }
}
