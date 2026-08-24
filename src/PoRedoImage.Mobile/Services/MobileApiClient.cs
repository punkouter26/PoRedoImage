using System.Net.Http.Json;
using PoRedoImage.Shared.DTOs;
using PoRedoImage.Shared.Json;

namespace PoRedoImage.Mobile.Services;

/// <summary>
/// Handles direct HTTP communication with the PoRedoImage backend (no authentication required for mobile).
/// </summary>
public class MobileApiClient : IMobileApiClient
{
    private readonly IMobileSettingsService _settings;
    private HttpClient? _client;
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
            _client = new HttpClient
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
        // Authentication is not required for the mobile client; verify connectivity
        return await PingAsync(ct);
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
