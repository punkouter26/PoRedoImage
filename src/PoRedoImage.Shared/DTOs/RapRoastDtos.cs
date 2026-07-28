using System.ComponentModel.DataAnnotations;

namespace PoRedoImage.Shared.DTOs;

/// <summary>
/// Request to turn a photo into a roast rap track.
/// </summary>
public class RapRoastRequest
{
    /// <summary>Base64-encoded image bytes.</summary>
    [Required]
    public string ImageData { get; set; } = string.Empty;

    [Required]
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    /// Optional vision model id, routed by <c>IVisionServiceRouter</c> exactly as the analyze
    /// pipeline does. Null falls back to the default backend.
    /// </summary>
    public string? ModelId { get; set; }

    /// <summary>
    /// Musical direction for the track. Kept client-settable so the same lyrics can be re-cut in a
    /// different style without re-running vision.
    /// </summary>
    public RapStyle Style { get; set; } = RapStyle.BoomBap;
}

/// <summary>Backing-track style. An enum, not a free string (§1 "zero magic strings").</summary>
public enum RapStyle
{
    /// <summary>Classic 90s boom-bap: dusty drums, vinyl crackle, ~90 BPM.</summary>
    BoomBap = 0,

    /// <summary>Modern trap: 808 sub-bass, hi-hat rolls, ~140 BPM half-time.</summary>
    Trap = 1,

    /// <summary>Upbeat old-school party rap: funk break, horn stabs, ~105 BPM.</summary>
    OldSchool = 2,
}

/// <summary>
/// Result of a roast run. <see cref="Lyrics"/> is always populated; <see cref="AudioData"/> is not,
/// because the music provider's safety filter can decline a prompt (see <see cref="AudioRefused"/>).
/// </summary>
public class RapRoastResponse
{
    /// <summary>The bars, including the <c>[Verse]</c> / <c>[Chorus]</c> section tags.</summary>
    public string Lyrics { get; set; } = string.Empty;

    /// <summary>Base64-encoded audio, or empty when <see cref="AudioRefused"/> is true.</summary>
    public string AudioData { get; set; } = string.Empty;

    /// <summary>MIME type of <see cref="AudioData"/>, e.g. <c>audio/mpeg</c>.</summary>
    public string AudioContentType { get; set; } = string.Empty;

    /// <summary>
    /// True when the music provider declined to perform the lyrics even after one softened retry.
    /// The client renders the lyrics on their own in that case rather than showing an error.
    /// </summary>
    public bool AudioRefused { get; set; }

    /// <summary>Provider-supplied refusal explanation, when there was one.</summary>
    public string? RefusalReason { get; set; }

    /// <summary>True when the lyrics shown are the softened second attempt.</summary>
    public bool LyricsSoftened { get; set; }

    /// <summary>The image description the lyrics were written from — shown as "what the AI saw".</summary>
    public string ImageDescription { get; set; } = string.Empty;

    /// <summary>
    /// True when a vision model produced <see cref="ImageDescription"/>. False means it fell back to
    /// the vision backend's tag-derived text, which yields noticeably blander lyrics — surfaced in
    /// the UI so a generic roast is explainable rather than mysterious.
    /// </summary>
    public bool DescriptionIsDetailed { get; set; }

    public long TotalMs { get; set; }
}
