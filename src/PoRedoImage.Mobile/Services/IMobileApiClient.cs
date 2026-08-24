using PoRedoImage.Shared.DTOs;

namespace PoRedoImage.Mobile.Services;

/// <summary>
/// Client interface for communicating with the PoRedoImage backend API from the mobile device.
/// </summary>
public interface IMobileApiClient
{
    /// <summary>
    /// Checks server liveness and reachability.
    /// </summary>
    Task<bool> PingAsync(CancellationToken ct = default);

    /// <summary>
    /// Performs the Dev/Guest login handshake and caches the antiforgery token.
    /// </summary>
    Task<bool> EnsureAuthenticatedAsync(CancellationToken ct = default);

    /// <summary>
    /// Generates a viral meme from a captured photo.
    /// </summary>
    Task<ImageAnalysisResponse> ProcessMemeAsync(ImageCaptureResult image, CancellationToken ct = default);

    /// <summary>
    /// Regenerates/reimagines the captured photo using AI and optional artistic style prompt.
    /// </summary>
    Task<ImageAnalysisResponse> ProcessRegenerationAsync(ImageCaptureResult image, string? stylePrompt = null, CancellationToken ct = default);

    /// <summary>
    /// Performs a Rap Roast on the photo, generating rhyme lyrics and backing audio.
    /// </summary>
    Task<RapRoastResponse> ProcessRapRoastAsync(
        ImageCaptureResult image,
        RapStyle style = RapStyle.BoomBap,
        RoastIntensity intensity = RoastIntensity.Roast,
        CancellationToken ct = default);

    /// <summary>
    /// Analyzes the visual scene using GPT-4o vision to generate descriptive captions.
    /// </summary>
    Task<string> DescribeImageAsync(ImageCaptureResult image, CancellationToken ct = default);
}

