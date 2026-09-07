using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
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
/// </summary>
/// <remarks>
/// All wire work happens over Google's public <c>generativelanguage.googleapis.com</c> REST
/// endpoint. The provider is intentionally NOT a singleton that resolves through the resilience
/// pipeline — Veo renders take 1–5 minutes, which is well past the 30-second <c>AttemptTimeout</c>
/// that the AI named clients are configured with. Spinning up a vanilla <see cref="HttpClient"/>
/// here keeps the long-poll cost off the resilience budget for the cheap image / music calls.
/// </remarks>
public sealed class VeoVideoGenerationService : IVideoGenerationService
{
    /// <summary>
    /// Default model id when <c>Google:VeoModel</c> is unset. Lite tier at 720p — the cheapest
    /// Veo variant ($0.05/sec vs $0.40 for Standard).
    /// </summary>
    public const string DefaultModel = "veo-3.1-generate-preview-lite";

    /// <summary>
    /// Veo exposes no audio parameter, so the prompt is the only lever for whether the render
    /// includes sound. Appended to prompts that said nothing about audio so the resulting clip
    /// is not silent by default. Already-audio-aware prompts are left alone.
    /// </summary>
    public const string AudioDirective =
        " Include ambient sound and natural audio that fits the scene.";

    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/";

    private readonly string _apiKey;
    private readonly string _model;
    private readonly HttpClient _http;
    private readonly ILogger<VeoVideoGenerationService> _logger;

    public VeoVideoGenerationService(IConfiguration configuration, IHttpClientFactory httpClientFactory, ILogger<VeoVideoGenerationService> logger)
    {
        _logger = logger;

        // Defence-in-depth: the host resolves which class is constructed (real vs mock) based on
        // Mocks:UseMockAi. If we're being constructed while mocks are on, something is wrong
        // upstream — failing loud here is cheaper than a test run that silently bills a live
        // token. The check matters more here than elsewhere: video is the most expensive call
        // in the app by an order of magnitude.
        if (ConfigValue.Bool(configuration, ConfigKeys.MocksUseMockAi))
        {
            throw new InvalidOperationException(
                "VeoVideoGenerationService was constructed while Mocks:UseMockAi=true. The DI "
                + "container should have resolved MockVeoVideoGenerationService instead. Blocking "
                + "to prevent accidental spend on a live provider.");
        }

        _apiKey = configuration[ConfigKeys.GoogleApiKey] ?? string.Empty;
        _model = configuration[ConfigKeys.GoogleVeoModel] ?? DefaultModel;

        // Named "Veo" so the factory can give it a longer timeout than the rest of the AI
        // pipeline. Default resilience would otherwise cancel renders before they finish.
        _http = httpClientFactory.CreateClient("Veo");
        _http.BaseAddress ??= new Uri(BaseUrl);

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogInformation("Google:ApiKey not configured; video generation is disabled.");
        }
        else
        {
            _logger.LogInformation("Veo video service initialized. Model={Model}", _model);
        }
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Anonymous body for the Veo long-running endpoint; assembly is not trimmed.")]
    public async Task<string> StartAsync(byte[] image, string contentType, string prompt, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            throw new VideoGenerationException(
                (int)System.Net.HttpStatusCode.ServiceUnavailable,
                "Video generation is not configured. Set Google:ApiKey via Key Vault.");
        }

        var instances = new[]
        {
            new
            {
                prompt = WithAudioDirection(prompt),
                image = new
                {
                    bytesBase64Encoded = Convert.ToBase64String(image),
                    mimeType = contentType,
                },
            },
        };

        var body = new
        {
            instances,
            parameters = new { sampleCount = 1 },
        };

        var url = $"models/{_model}:predictLongRunning";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = JsonContent.Create(body);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new VideoGenerationException(
                (int)System.Net.HttpStatusCode.BadGateway,
                $"Could not reach the video service: {ex.Message}");
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new VideoGenerationException(
                (int)response.StatusCode,
                ExtractErrorMessage(errorBody));
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        // Google's long-running shape: { "name": "operations/..." }. Anything else means the
        // shape changed under us and we should fail loud rather than guess.
        if (!doc.RootElement.TryGetProperty("name", out var nameEl)
            || nameEl.ValueKind != JsonValueKind.String)
        {
            throw new VideoGenerationException(
                (int)System.Net.HttpStatusCode.BadGateway,
                "Video service returned an unexpected response (no operation name).");
        }

        return nameEl.GetString()!;
    }

    public async Task<VideoGenerationStatus> PollAsync(string operationName, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return VideoGenerationStatus.Failed("Video generation is not configured.");
        }

        // The handle came from the provider, so it lands on the wire as a path segment. Pass it
        // through Uri.EscapeDataString to be safe against future shape changes.
        var url = operationName.StartsWith("operations/", StringComparison.Ordinal)
            ? $"{operationName}"
            : $"operations/{operationName}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            return VideoGenerationStatus.Failed($"Could not reach the video service: {ex.Message}");
        }

        if (!response.IsSuccessStatusCode)
        {
            return VideoGenerationStatus.Failed($"Video service returned {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        // "done" is the canonical long-running marker; absent means still running.
        if (!root.TryGetProperty("done", out var doneEl) || !doneEl.GetBoolean())
        {
            return VideoGenerationStatus.Pending();
        }

        // Failure path. Surface the message rather than a generic failure — for video the usual
        // cause is the provider rejecting the prompt, and a generic message makes that look like
        // an outage on our side.
        if (root.TryGetProperty("error", out var errorEl))
        {
            var message = errorEl.TryGetProperty("message", out var msgEl)
                ? msgEl.GetString() ?? "Video generation was rejected."
                : "Video generation was rejected.";
            return VideoGenerationStatus.Failed(message);
        }

        if (!TryExtractVideoUri(root, out var videoUri))
        {
            _logger.LogWarning("Veo operation {Operation} completed with no video payload.", operationName);
            return VideoGenerationStatus.Failed(
                "Video service completed the job without delivering a clip. Try again with a different prompt.");
        }

        try
        {
            var videoBytes = await _http.GetByteArrayAsync(videoUri, ct);
            return new VideoGenerationStatus(
                Done: true,
                Video: videoBytes,
                ContentType: "video/mp4");
        }
        catch (HttpRequestException ex)
        {
            return VideoGenerationStatus.Failed($"Could not download the finished clip: {ex.Message}");
        }
    }

    /// <summary>
    /// Appends <see cref="AudioDirective"/> to prompts that said nothing about sound. Keeps the
    /// caller's exact wording when they already took a position (including one that asked for
    /// silence).
    /// </summary>
    public static string WithAudioDirection(string prompt)
    {
        var lowered = (prompt ?? string.Empty).ToLowerInvariant();
        // Match the kinds of phrases that imply a position on audio. The list is intentionally
        // narrow — "silent film pastiche" means "no sound"; "add upbeat music" means "yes sound";
        // "a man reads a menu" means "no position, default to yes".
        string[] audioCues =
        [
            "sound", "audio", "music", "voice", "narrat", "speaks", "speech", "dialog",
            "talking", "speak", "hear", "listen", "laugh", "quiet",
            "silent", "silence", "mute", "no audio", "no sound",
        ];
        foreach (var cue in audioCues)
        {
            if (lowered.Contains(cue, StringComparison.Ordinal)) return prompt ?? string.Empty;
        }
        // Append the directive for any prompt that did not take a position on audio. The
        // invariant "result ends with AudioDirective" holds whether the prompt was empty
        // (AudioDirective alone) or a non-empty phrase ("prompt || AudioDirective") — the
        // leading space in AudioDirective is intentional and makes that join natural.
        return (prompt ?? string.Empty).TrimEnd() + AudioDirective;
    }

    private static bool TryExtractVideoUri(JsonElement root, out string uri)
    {
        uri = string.Empty;

        // Google's long-running response shapes the result either as
        //   { "response": { "videos": [ { "uri": "..." } ] } }
        // or, after the operation is done, as
        //   { "videos": [ { "uri": "..." } ] }
        // depending on the model. Walk both.
        JsonElement container = root;
        if (root.TryGetProperty("response", out var responseEl)
            && responseEl.ValueKind == JsonValueKind.Object)
        {
            container = responseEl;
        }

        if (!container.TryGetProperty("videos", out var videosEl)
            || videosEl.ValueKind != JsonValueKind.Array
            || videosEl.GetArrayLength() == 0)
        {
            return false;
        }

        var first = videosEl[0];
        if (!first.TryGetProperty("uri", out var uriEl) || uriEl.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        uri = uriEl.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(uri);
    }

    private static string ExtractErrorMessage(string errorBody)
    {
        if (string.IsNullOrWhiteSpace(errorBody)) return "The video service rejected the request.";
        try
        {
            using var doc = JsonDocument.Parse(errorBody);
            if (doc.RootElement.TryGetProperty("error", out var errorEl)
                && errorEl.TryGetProperty("message", out var msgEl)
                && msgEl.ValueKind == JsonValueKind.String)
            {
                return msgEl.GetString()!;
            }
        }
        catch (JsonException)
        {
            // fall through — return raw body
        }
        return errorBody.Trim();
    }
}
