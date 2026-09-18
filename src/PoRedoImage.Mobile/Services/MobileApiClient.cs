using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using PoRedoImage.Domain.Entities;
using PoRedoImage.Mobile.Models;
using PoRedoImage.Shared.DTOs;
using PoRedoImage.Shared.Json;
using ImageCaptureResult = PoRedoImage.Mobile.Models.ImageCaptureResult;

namespace PoRedoImage.Mobile.Services;

/// <summary>
/// Handles direct HTTP communication with the PoRedoImage backend.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors the web client's BFF contract. Generation endpoints (<c>/api/images/analyze</c>,
/// <c>/api/rap-roast</c>, <c>/api/bulk-generate</c>) are anonymous and rate-limited server-side;
/// gallery (<c>/api/user-images</c>) and video (<c>/api/video</c>) require the guest session
/// cookie plus the <c>X-CSRF-TOKEN</c> header, which this client fetches from
/// <c>/api/antiforgery/token</c> exactly as <c>AntiforgeryTokenHandler</c> does in the browser.
/// </para>
/// <para>
/// The session handshake only works against Development/Test servers — <c>/dev-login</c> is
/// blocked in Production by design. When it fails, <see cref="HasSession"/> stays false and the
/// UI disables gallery/video honestly instead of letting a save fail mid-upload.
/// </para>
/// </remarks>
public class MobileApiClient : IMobileApiClient
{
    private const string CsrfHeaderName = "X-CSRF-TOKEN";
    private const string IdempotencyHeaderName = "Idempotency-Key";

    private readonly IMobileSettingsService _settings;
    private readonly CookieContainer _cookies = new();
    private readonly SemaphoreSlim _csrfGate = new(1, 1);
    private HttpClient? _client;
    private Uri? _lastBaseUri;

    // Identity-bound: fetched after the login handshake, replayed once on a 400 — the same
    // load-bearing retry AntiforgeryTokenHandler performs, for the same reason.
    private string? _csrfToken;
    private Uri? _sessionBaseUri;

    private sealed record AntiforgeryTokenResponse(string Token);
    private sealed record VideoStartResponse(string OperationName);

    public MobileApiClient(IMobileSettingsService settings)
    {
        _settings = settings;
    }

    /// <inheritdoc />
    public bool HasSession => _sessionBaseUri is not null && _sessionBaseUri == _lastBaseUri;

    private HttpClient GetOrCreateClient()
    {
        var baseUri = _settings.GetBaseUri();
        if (_client == null || _lastBaseUri != baseUri)
        {
            _client?.Dispose();
            var handler = new SocketsHttpHandler
            {
                CookieContainer = _cookies,
                UseCookies = true,
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(10)
            };
            _client = new HttpClient(handler)
            {
                BaseAddress = baseUri,
                Timeout = TimeSpan.FromSeconds(90)
            };
            _lastBaseUri = baseUri;
        }
        return _client;
    }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        try
        {
            var client = GetOrCreateClient();
            using var response = await client.GetAsync("alive", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> EnsureAuthenticatedAsync(CancellationToken ct = default)
    {
        var baseUri = _settings.GetBaseUri();
        if (_sessionBaseUri == baseUri && !string.IsNullOrEmpty(_csrfToken))
            return true;

        // Stable per install: the settings service mints a GUEST#### id on first read, which is
        // the identity /dev-login registers server-side.
        var guestId = _settings.GuestId;

        try
        {
            // Dev/Test-only route: sets the auth cookie for this guest identity. On a Production
            // server it does not exist and we return false — the UI then shows why gallery and
            // video are unavailable instead of failing the save halfway through.
            using var login = await GetOrCreateClient().GetAsync(
                $"dev-login?guestId={Uri.EscapeDataString(guestId)}", ct);
            if (!login.IsSuccessStatusCode)
                return false;

            _csrfToken = await FetchCsrfTokenAsync(ct);
            _sessionBaseUri = baseUri;
            return !string.IsNullOrEmpty(_csrfToken);
        }
        catch
        {
            return false;
        }
    }

    private async Task<string> FetchCsrfTokenAsync(CancellationToken ct)
    {
        await _csrfGate.WaitAsync(ct);
        try
        {
            using var response = await GetOrCreateClient().GetAsync("api/antiforgery/token", ct);
            response.EnsureSuccessStatusCode();
            var dto = await response.Content.ReadFromJsonAsync<AntiforgeryTokenResponse>(
                SharedJsonOptions.Default, ct);
            return dto?.Token ?? throw new InvalidOperationException(
                "Antiforgery token endpoint returned no token.");
        }
        finally
        {
            _csrfGate.Release();
        }
    }

    public async Task<ImageAnalysisResponse> ProcessMemeAsync(ImageCaptureResult image, CancellationToken ct = default)
    {
        var request = new ImageAnalysisRequest
        {
            ImageData = image.Base64Data,
            ContentType = image.ContentType,
            FileName = image.FileName,
            Mode = ProcessingMode.MemeGeneration,
            DescriptionLength = 200
        };

        return await SendPostAsync<ImageAnalysisRequest, ImageAnalysisResponse>(
            "api/images/analyze", request, ct);
    }

    public async Task<RapRoastResponse> ProcessRapRoastAsync(
        ImageCaptureResult image,
        RapStyle style = RapStyle.StandUp,
        RoastIntensity intensity = RoastIntensity.Roast,
        CancellationToken ct = default)
    {
        var request = new RapRoastRequest
        {
            ImageData = image.Base64Data,
            ContentType = image.ContentType,
            Style = style,
            Intensity = intensity
        };

        return await SendPostAsync<RapRoastRequest, RapRoastResponse>(
            "api/rap-roast", request, ct);
    }

    public async Task<string> DescribeImageAsync(ImageCaptureResult image, CancellationToken ct = default)
    {
        var request = new BulkDescribeRequest(image.Base64Data, image.ContentType);
        var response = await SendPostAsync<BulkDescribeRequest, BulkDescribeResponse>(
            "api/bulk-generate/describe", request, ct);

        return response.Description;
    }

    public IAsyncEnumerable<BulkBatchItem> GenerateBatchAsync(
        ImageCaptureResult image, IReadOnlyList<string> prompts, CancellationToken ct = default)
    {
        // Anonymous + rate-limited server-side: no cookie or CSRF header required, same as the
        // web client's call. NDJSON — one BulkBatchItem per line, slots landing out of order.
        // The body is fetched synchronously inside the returned enumerable, so cancellation
        // applies once the consumer starts iterating; the POST itself is kicked off immediately
        // and disposed when the response is read.
        return GenerateBatchInternalAsync(image, prompts, ct);
    }

    private async IAsyncEnumerable<BulkBatchItem> GenerateBatchInternalAsync(
        ImageCaptureResult image, IReadOnlyList<string> prompts,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var request = new BulkBatchRequest(image.Base64Data, image.ContentType, prompts.ToArray(), null);
        var client = GetOrCreateClient();
        using var response = await client.PostAsJsonAsync(
            "api/bulk-generate/batch", request, SharedJsonOptions.Default, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Bulk generation failed ({response.StatusCode}): {errorContent}");
        }

        await foreach (var item in ReadNdjsonAsync(response, ct))
            yield return item;
    }

    private static async IAsyncEnumerable<BulkBatchItem> ReadNdjsonAsync(
        HttpResponseMessage response, [EnumeratorCancellation] CancellationToken ct)
    {
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var item = JsonSerializer.Deserialize<BulkBatchItem>(line, SharedJsonOptions.Default);
            if (item is not null)
                yield return item;
        }
    }

    public async Task<string> StartVideoAsync(ImageCaptureResult image, string prompt, CancellationToken ct = default)
    {
        var request = new VideoGenerateRequest(image.Base64Data, image.ContentType, prompt);
        using var response = await PostProtectedAsync("api/video/generate", request, ct);
        var result = await response.Content.ReadFromJsonAsync<VideoStartResponse>(SharedJsonOptions.Default, ct);
        return result?.OperationName ?? throw new InvalidOperationException(
            "Video endpoint returned no operation handle.");
    }

    public async Task<VideoGenerateStatusResponse> PollVideoAsync(string operationName, CancellationToken ct = default)
    {
        var client = GetOrCreateClient();
        using var response = await client.GetAsync(
            $"api/video/status?op={Uri.EscapeDataString(operationName)}", ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Video status failed ({response.StatusCode}): {errorContent}");
        }

        var status = await response.Content.ReadFromJsonAsync<VideoGenerateStatusResponse>(
            SharedJsonOptions.Default, ct);

        return status ?? new VideoGenerateStatusResponse(Done: true, ErrorMessage: "Empty response from the server.");
    }

    public async Task<SaveImageResponse> SaveOriginalToGalleryAsync(ImageCaptureResult image, CancellationToken ct = default)
    {
        var request = new SaveOriginalRequest(image.Base64Data, image.ContentType, image.FileName);
        using var response = await PostProtectedAsync("api/user-images/original", request, ct);
        var result = await response.Content.ReadFromJsonAsync<SaveImageResponse>(SharedJsonOptions.Default, ct);
        return result ?? throw new InvalidOperationException("Gallery save returned no response body.");
    }

    public async Task<SaveImageResponse> SaveResultToGalleryAsync(
        byte[] imageBytes, string contentType, UserImageKind kind, CancellationToken ct = default)
    {
        var request = new SaveResultRequest(Convert.ToBase64String(imageBytes), contentType, kind);
        using var response = await PostProtectedAsync("api/user-images/result", request, ct);
        var result = await response.Content.ReadFromJsonAsync<SaveImageResponse>(SharedJsonOptions.Default, ct);
        return result ?? throw new InvalidOperationException("Gallery save returned no response body.");
    }

    public async Task<IReadOnlyList<UserImageDto>> ListGalleryAsync(CancellationToken ct = default)
    {
        var client = GetOrCreateClient();
        using var response = await client.GetAsync("api/user-images/", ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Gallery load failed ({response.StatusCode}): {errorContent}");
        }

        var images = await response.Content.ReadFromJsonAsync<List<UserImageDto>>(SharedJsonOptions.Default, ct);
        return images ?? [];
    }

    public async Task<byte[]> GetGalleryImageBytesAsync(string id, CancellationToken ct = default)
    {
        var client = GetOrCreateClient();
        using var response = await client.GetAsync($"api/user-images/{Uri.EscapeDataString(id)}", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    public async Task DeleteGalleryImageAsync(string id, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/user-images/{Uri.EscapeDataString(id)}");
        using var response = await SendProtectedAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Gallery delete failed ({response.StatusCode}): {errorContent}");
        }
    }

    /// <summary>
    /// POSTs JSON with the CSRF header and an Idempotency-Key, replaying once on 400 with a
    /// fresh token — tokens are identity-bound, so one cached before the handshake landed can
    /// legitimately go stale (the same single-retry contract the web client runs).
    /// </summary>
    private async Task<HttpResponseMessage> PostProtectedAsync<TRequest>(
        string endpoint, TRequest request, CancellationToken ct) where TRequest : class
    {
        var client = GetOrCreateClient();
        var idempotencyKey = Guid.NewGuid().ToString();

        async Task<HttpRequestMessage> BuildAsync()
        {
            var msg = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(request, options: SharedJsonOptions.Default)
            };
            msg.Headers.TryAddWithoutValidation(IdempotencyHeaderName, idempotencyKey);
            if (!string.IsNullOrEmpty(_csrfToken))
                msg.Headers.TryAddWithoutValidation(CsrfHeaderName, _csrfToken);
            return msg;
        }

        var response = await client.SendAsync(await BuildAsync(), ct);
        if (response.StatusCode != HttpStatusCode.BadRequest)
            return response;

        // Stale identity-bound token: refetch once and replay. Disposed because the retry supersedes it.
        response.Dispose();
        _csrfToken = await FetchCsrfTokenAsync(ct);
        return await client.SendAsync(await BuildAsync(), ct);
    }

    /// <summary>
    /// Sends an arbitrary unsafe request with CSRF stamping and the same one-shot 400 retry.
    /// </summary>
    private async Task<HttpResponseMessage> SendProtectedAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var client = GetOrCreateClient();
        if (!string.IsNullOrEmpty(_csrfToken))
            request.Headers.TryAddWithoutValidation(CsrfHeaderName, _csrfToken);

        var response = await client.SendAsync(request, ct);
        if (response.StatusCode != HttpStatusCode.BadRequest)
            return response;

        _csrfToken = await FetchCsrfTokenAsync(ct);
        var retry = await CloneRequestAsync(request, _csrfToken);
        response.Dispose();
        return await client.SendAsync(retry, ct);
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage request, string? token)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        if (request.Content is not null)
        {
            var buffer = await request.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(buffer);
            foreach (var header in request.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        if (!string.IsNullOrEmpty(token))
            clone.Headers.TryAddWithoutValidation(CsrfHeaderName, token);
        return clone;
    }

    private async Task<TResponse> SendPostAsync<TRequest, TResponse>(
        string endpoint, TRequest request, CancellationToken ct) where TResponse : class
    {
        var client = GetOrCreateClient();

        using var response = await client.PostAsJsonAsync(endpoint, request, SharedJsonOptions.Default, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"API request failed ({response.StatusCode}): {errorContent}");
        }

        var result = await response.Content.ReadFromJsonAsync<TResponse>(SharedJsonOptions.Default, ct);
        if (result == null)
            throw new InvalidOperationException("Empty response received from the server.");

        return result;
    }
}
