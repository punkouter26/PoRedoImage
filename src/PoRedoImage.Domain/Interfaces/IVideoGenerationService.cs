namespace PoRedoImage.Domain.Interfaces;

/// <summary>
/// Vendor-agnostic abstraction for generating a short video from a still image plus a prompt.
/// </summary>
/// <remarks>
/// The provider (Veo) takes 1–5 minutes to render, so the surface is split rather than one
/// blocking <c>GenerateAsync</c>: <see cref="StartAsync"/> submits the job and returns the
/// provider's operation handle immediately; <see cref="PollAsync"/> polls that handle. This
/// matches the wire contract the BFF exposes (<c>POST /api/video/generate</c> +
/// <c>GET /api/video/status</c>) and keeps the per-call latency off the client UI thread.
/// </remarks>
public interface IVideoGenerationService
{
    /// <summary>True when the provider is configured and accepts jobs.</summary>
    bool IsConfigured { get; }

    /// <summary>Submit a video render job.</summary>
    /// <param name="image">Source image bytes — the first frame the video animates from.</param>
    /// <param name="contentType">MIME type of <paramref name="image"/>.</param>
    /// <param name="prompt">What should happen in the clip.</param>
    /// <returns>The provider's operation handle — pass back to <see cref="PollAsync"/>.</returns>
    Task<string> StartAsync(byte[] image, string contentType, string prompt, CancellationToken ct = default);

    /// <summary>
    /// Checks a job started by <see cref="StartAsync"/>, returning the video once it is ready.
    /// </summary>
    Task<VideoGenerationStatus> PollAsync(string operationName, CancellationToken ct = default);
}

/// <summary>
/// The provider refused or could not accept a <see cref="IVideoGenerationService.StartAsync"/> call,
/// or the call returned a 4xx/5xx worth surfacing as a specific reason rather than a generic failure.
/// </summary>
/// <remarks>
/// A rejection at submit time used to surface as a bare "Could not start video generation." while
/// the provider told us exactly why (filtered photo, exhausted quota, malformed prompt). Catching
/// this exception at the endpoint boundary lets the API pass the reason through verbatim.
/// </remarks>
public sealed class VideoGenerationException(int status, string message)
    : Exception(message)
{
    public int Status { get; } = status;
}

/// <summary>State of a single video-generation job.</summary>
public sealed record VideoGenerationStatus(
    bool Done,
    byte[]? Video = null,
    string? ContentType = null,
    string? ErrorMessage = null)
{
    public static VideoGenerationStatus Pending() => new(Done: false);
    public static VideoGenerationStatus Failed(string message) => new(Done: true, ErrorMessage: message);
}
