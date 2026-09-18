using PoRedoImage.Domain.Entities;
using PoRedoImage.Mobile.Models;
using PoRedoImage.Shared.DTOs;
using ImageCaptureResult = PoRedoImage.Mobile.Models.ImageCaptureResult;

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
    /// Establishes the guest session (Dev/Test servers only — <c>/dev-login</c> is blocked in
    /// Production) and caches the identity-bound antiforgery token. Returns false when the server
    /// refuses; gallery/video features stay disabled honestly rather than failing mid-save.
    /// </summary>
    Task<bool> EnsureAuthenticatedAsync(CancellationToken ct = default);

    /// <summary>
    /// True once <see cref="EnsureAuthenticatedAsync"/> succeeded against the current server.
    /// </summary>
    bool HasSession { get; }

    /// <summary>
    /// Generates a viral meme from a captured photo.
    /// </summary>
    Task<ImageAnalysisResponse> ProcessMemeAsync(ImageCaptureResult image, CancellationToken ct = default);

    /// <summary>
    /// Performs a Rap Roast on the photo, generating rhyme lyrics and backing audio.
    /// </summary>
    Task<RapRoastResponse> ProcessRapRoastAsync(
        ImageCaptureResult image,
        RapStyle style = RapStyle.StandUp,
        RoastIntensity intensity = RoastIntensity.Roast,
        CancellationToken ct = default);

    /// <summary>
    /// Analyzes the visual scene using the server's vision model to generate descriptive captions.
    /// </summary>
    Task<string> DescribeImageAsync(ImageCaptureResult image, CancellationToken ct = default);

    /// <summary>
    /// Streams a bulk generation batch as each slot lands (NDJSON, same wire format the web
    /// client consumes). Slots complete out of order; one slot failing never fails the batch.
    /// </summary>
    IAsyncEnumerable<BulkBatchItem> GenerateBatchAsync(
        ImageCaptureResult image,
        IReadOnlyList<string> prompts,
        CancellationToken ct = default);

    /// <summary>
    /// Starts an image-to-video render and returns the provider operation handle. Poll it with
    /// <see cref="PollVideoAsync"/> — Veo takes 1–5 minutes, so nothing here blocks.
    /// </summary>
    Task<string> StartVideoAsync(ImageCaptureResult image, string prompt, CancellationToken ct = default);

    /// <summary>
    /// Polls a video render started by <see cref="StartVideoAsync"/>.
    /// </summary>
    Task<VideoGenerateStatusResponse> PollVideoAsync(string operationName, CancellationToken ct = default);

    /// <summary>
    /// Saves the uploaded original to the caller's PoRedo gallery.
    /// </summary>
    Task<SaveImageResponse> SaveOriginalToGalleryAsync(ImageCaptureResult image, CancellationToken ct = default);

    /// <summary>
    /// Saves an AI-processed result image to the caller's PoRedo gallery.
    /// </summary>
    Task<SaveImageResponse> SaveResultToGalleryAsync(
        byte[] imageBytes, string contentType, UserImageKind kind, CancellationToken ct = default);

    /// <summary>
    /// Lists the caller's PoRedo gallery in the order the server returns it.
    /// </summary>
    Task<IReadOnlyList<UserImageDto>> ListGalleryAsync(CancellationToken ct = default);

    /// <summary>
    /// Downloads one gallery image's bytes (authenticated — the blob is not public).
    /// </summary>
    Task<byte[]> GetGalleryImageBytesAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Deletes one gallery image.
    /// </summary>
    Task DeleteGalleryImageAsync(string id, CancellationToken ct = default);
}

