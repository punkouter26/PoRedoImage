namespace PoRedoImage.Domain.Interfaces;

/// <summary>
/// Vendor-agnostic abstraction for generating a short video from a still image plus a prompt.
/// </summary>
/// <remarks>
/// Split into <see cref="StartAsync"/> / <see cref="PollAsync"/> rather than a single
/// <c>GenerateAsync</c>, because video generation takes 1–5 minutes and nothing in this stack can
/// hold a request open that long: Azure App Service drops an idle HTTP request at ~230 seconds, so
/// a blocking call would fail in production while passing locally. The provider's own
/// long-running-operation handle is the job id, which keeps the server stateless — there is no job
/// table to persist, expire, or clean up.
/// </remarks>
public interface IVideoGenerationService
{
    /// <summary>
    /// Submits a generation job and returns the provider's operation handle immediately.
    /// </summary>
    /// <param name="image">Source image bytes — the first frame the video animates from.</param>
    /// <param name="contentType">MIME type of <paramref name="image"/>.</param>
    /// <param name="prompt">What should happen in the clip.</param>
    /// <returns>An opaque operation handle to pass to <see cref="PollAsync"/>.</returns>
    /// <remarks>
    /// Clip length is not a parameter: every clip is 8 seconds, which is also the longest Veo will
    /// produce. Fixing it keeps the cost of one render a single known number rather than something
    /// the caller can vary, and removes a control the user has no reason to touch.
    /// </remarks>
    Task<string> StartAsync(
        byte[] image, string contentType, string prompt, CancellationToken ct = default);

    /// <summary>
    /// Checks a job started by <see cref="StartAsync"/>, returning the video once it is ready.
    /// </summary>
    Task<VideoGenerationStatus> PollAsync(string operationName, CancellationToken ct = default);

    /// <summary>True when the provider has the configuration it needs to be called.</summary>
    bool IsConfigured { get; }
}

/// <summary>
/// The provider refused or could not accept a <see cref="IVideoGenerationService.StartAsync"/> call,
/// carrying a message fit to show the user.
/// </summary>
/// <remarks>
/// A rejection at submit time used to surface as a bare "Could not start video generation." while
/// the actual reason — an invalid request shape, a filtered photo, an exhausted quota — sat in the
/// server log where the person who could act on it never sees it. Same rule as every other fallback
/// in this codebase: the failure has to name itself. <see cref="Message"/> is the provider's own
/// explanation, already trimmed of the raw JSON envelope.
/// </remarks>
public sealed class VideoGenerationException(int status, string message)
    : Exception(message)
{
    /// <summary>Upstream HTTP status, for the caller to map onto its own response.</summary>
    public int Status { get; } = status;
}

/// <summary>State of a single video-generation job.</summary>
/// <param name="Done">False while the provider is still rendering.</param>
/// <param name="Video">Encoded video bytes; null until <paramref name="Done"/> and successful.</param>
/// <param name="ContentType">MIME type of <paramref name="Video"/> (e.g. <c>video/mp4</c>).</param>
/// <param name="ErrorMessage">
/// Set when the job finished without a video — a safety-filter rejection, or a provider error.
/// Modelled in the result rather than thrown because a refusal is an expected outcome the user
/// needs to read, not a fault the caller can retry its way out of.
/// </param>
public sealed record VideoGenerationStatus(
    bool Done,
    byte[]? Video = null,
    string? ContentType = null,
    string? ErrorMessage = null)
{
    public static VideoGenerationStatus Pending() => new(Done: false);
    public static VideoGenerationStatus Failed(string reason) => new(Done: true, ErrorMessage: reason);
}
