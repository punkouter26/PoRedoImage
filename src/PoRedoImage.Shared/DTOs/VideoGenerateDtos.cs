using System.ComponentModel.DataAnnotations;

namespace PoRedoImage.Shared.DTOs;

/// <summary>
/// Request to turn a still image into a short clip. Posted to <c>POST /api/video/generate</c>,
/// which returns immediately with an operation handle rather than the finished video.
/// </summary>
/// <param name="ImageData">Base64 image bytes — the frame the clip animates from.</param>
/// <param name="ContentType">MIME type of <paramref name="ImageData"/>.</param>
/// <param name="Prompt">What should happen in the clip.</param>
/// <remarks>
/// There is no duration field: every clip is 8 seconds, which is also the longest Veo produces.
/// </remarks>
public sealed record VideoGenerateRequest(
    [property: Required] string ImageData,
    [property: Required] string ContentType,
    [property: Required, StringLength(1200, MinimumLength = 3)] string Prompt);

/// <summary>
/// Acknowledgement that a clip is rendering. <paramref name="OperationName"/> is the provider's
/// own job handle — the client passes it back to <c>GET /api/video/status</c> until done.
/// </summary>
public sealed record VideoGenerateStartResponse(string OperationName);

/// <summary>
/// One poll of a rendering job.
/// </summary>
/// <param name="Done">False while the clip is still rendering; keep polling.</param>
/// <param name="VideoData">Base64 video bytes once finished successfully.</param>
/// <param name="VideoContentType">MIME type of <paramref name="VideoData"/>.</param>
/// <param name="ErrorMessage">
/// Set when the job finished without a clip. Always populated on failure rather than left null:
/// a video that silently fails to appear reads to the user as the app being broken.
/// </param>
public sealed record VideoGenerateStatusResponse(
    bool Done,
    string? VideoData = null,
    string? VideoContentType = null,
    string? ErrorMessage = null);
