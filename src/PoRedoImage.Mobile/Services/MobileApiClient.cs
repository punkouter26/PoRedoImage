using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PoRedoImage.Shared.DTOs;
using PoRedoImage.Shared.Json;

namespace PoRedoImage.Mobile.Services;

/// <summary>
/// Handles authenticated HTTP communication with the PoRedoImage BFF host.
/// </summary>
public class MobileApiClient : IMobileApiClient
{
    private readonly IMobileSettingsService _settings;
    private readonly CookieContainer _cookieContainer = new();
    private readonly SemaphoreSlim _authLock = new(1, 1);
    private HttpClient? _client;
    private string? _csrfToken;
    private Uri? _lastBaseUri;

    public MobileApiClient(IMobileSettingsService settings)
    {
        _settings = settings;
    }

    private HttpClient GetOrCreateClient()
    {
        var baseUri = _settings.GetBaseUri();
        if (_client == null || _lastBaseUri != baseUri)
        {
            _client?.Dispose();
            var handler = new HttpClientHandler
            {
                CookieContainer = _cookieContainer,
                UseCookies = true,
                AllowAutoRedirect = true
            };

            _client = new HttpClient(handler)
            {
                BaseAddress = baseUri,
                Timeout = TimeSpan.FromSeconds(90)
            };
            _lastBaseUri = baseUri;
            _csrfToken = null; // Clear token on endpoint change
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
        await _authLock.WaitAsync(ct);
        try
        {
            var client = GetOrCreateClient();

            // 1. Establish guest session via /auth/login/fake (populates .AspNetCore.Cookies)
            var guestLoginUrl = $"auth/login/fake?guestId={Uri.EscapeDataString(_settings.GuestId)}";
            using var authResponse = await client.GetAsync(guestLoginUrl, ct);

            // 2. Fetch the CSRF token bound to this session
            using var tokenResponse = await client.GetAsync("api/antiforgery/token", ct);
            if (tokenResponse.IsSuccessStatusCode)
            {
                var content = await tokenResponse.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.TryGetProperty("token", out var tokenProp))
                {
                    _csrfToken = tokenProp.GetString();
                    return !string.IsNullOrEmpty(_csrfToken);
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
        finally
        {
            _authLock.Release();
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

    public async Task<ImageAnalysisResponse> ProcessRegenerationAsync(
        ImageCaptureResult image, string? stylePrompt = null, CancellationToken ct = default)
    {
        var request = new ImageAnalysisRequest
        {
            ImageData = image.Base64Data,
            ContentType = image.ContentType,
            FileName = image.FileName,
            Mode = ProcessingMode.ImageRegeneration,
            DescriptionLength = 250,
            PrecomputedEnhancedPrompt = stylePrompt
        };

        return await SendPostAsync<ImageAnalysisRequest, ImageAnalysisResponse>(
            "api/images/analyze", request, ct);
    }

    public async Task<RapRoastResponse> ProcessRapRoastAsync(
        ImageCaptureResult image,
        RapStyle style = RapStyle.BoomBap,
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

    private async Task<TResponse> SendPostAsync<TRequest, TResponse>(
        string endpoint, TRequest request, CancellationToken ct) where TResponse : class
    {
        var client = GetOrCreateClient();

        if (string.IsNullOrEmpty(_csrfToken))
        {
            var authOk = await EnsureAuthenticatedAsync(ct);
            if (!authOk)
                throw new InvalidOperationException("Failed to connect or authenticate with the backend server. Check server address in Settings.");
        }

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(request, options: SharedJsonOptions.Default)
        };

        if (!string.IsNullOrEmpty(_csrfToken))
        {
            requestMessage.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", _csrfToken);
        }

        var response = await client.SendAsync(requestMessage, ct);

        // On 400 (expired/stale token) or 401, refresh token and retry once
        if (response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _csrfToken = null;
            await EnsureAuthenticatedAsync(ct);

            using var retryMessage = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(request, options: SharedJsonOptions.Default)
            };

            if (!string.IsNullOrEmpty(_csrfToken))
                retryMessage.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", _csrfToken);

            response.Dispose();
            response = await client.SendAsync(retryMessage, ct);
        }

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

