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
    /// Delivery for the track. Kept client-settable so the same photo can be re-cut in a different
    /// style without re-running vision.
    /// </summary>
    public RapStyle Style { get; set; } = RapStyle.StandUp;

    /// <summary>
    /// How hard the bars hit. Absent from the payload it stays <see cref="RoastIntensity.Roast"/>,
    /// which is the tone the feature shipped with — System.Text.Json leaves a property at its
    /// initializer when the JSON omits it, so an older client keeps its old behaviour exactly.
    /// </summary>
    public RoastIntensity Intensity { get; set; } = RoastIntensity.Roast;

    /// <summary>
    /// Allow profanity in the bars. Off by default, so an older client and a first-time visitor both
    /// get the clean cut they used to get.
    /// </summary>
    /// <remarks>
    /// Deliberately separate from <see cref="Intensity"/> rather than a fourth level on it. Language
    /// and target are independent axes — a Gentle roast can swear affectionately and a Scorched one
    /// can be brutal in clean words — and welding them together is what made "harder language" and
    /// "meaner subject" the same request. Neither this flag nor the dial widens what may be roasted.
    /// <para>
    /// Expect a lower audio yield with this on: Lyria filters the sung lyrics, so an explicit draft
    /// is the likeliest to be refused, and the orchestrator's one retry answers that by dropping the
    /// profanity rather than the punchlines.
    /// </para>
    /// </remarks>
    public bool ExplicitLanguage { get; set; }
}

/// <summary>
/// How sharp the roast is allowed to get. Orthogonal to <see cref="RapStyle"/>: intensity steers the
/// words, style steers the beat.
/// </summary>
/// <remarks>
/// Every level sits inside the same content guardrail — the dial changes how hard the jab lands, never
/// what it is allowed to land on. <see cref="Scorched"/> is not permission to cross into
/// characteristics; it is permission to be merciless about choices. Profanity rides on
/// <see cref="RapRoastRequest.ExplicitLanguage"/>, not on this.
/// </remarks>
public enum RoastIntensity
{
    /// <summary>Affectionate teasing. Warm enough to show your mum.</summary>
    Gentle = 0,

    /// <summary>Real punchlines, good-natured. The default, and the tone the feature shipped with.</summary>
    Roast = 1,

    /// <summary>Merciless — surgical burns, still aimed only at choices.</summary>
    Scorched = 2,

    /// <summary>
    /// The ceiling. A battle verse written to end somebody, with no joke spared and no warmth
    /// anywhere in it — still aimed only at choices, because that limit is not on the dial.
    /// </summary>
    Nuclear = 3,
}

/// <summary>
/// How the roast is delivered. An enum, not a free string (§1 "zero magic strings").
/// </summary>
/// <remarks>
/// No longer purely a backing track: <see cref="StandUp"/> changes the written FORM as well as the
/// audio, so this reaches the lyric writer's system prompt and not just its style line. Serialized
/// as its numeric value, so replacing the member at 0 keeps the wire contract older clients use.
/// </remarks>
public enum RapStyle
{
    /// <summary>
    /// A spoken stand-up roast set in a club rather than a verse over a beat. Prose punchlines, no
    /// forced rhyme.
    /// </summary>
    StandUp = 0,

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

    /// <summary>
    /// True when the user asked for explicit language but the softened retry had to strip it. Lets
    /// the UI explain a clean track the user did not ask for, instead of the toggle looking broken.
    /// </summary>
    public bool ExplicitDropped { get; set; }

    /// <summary>
    /// Why the bars are the deterministic stock verse rather than model-written. Null when a model
    /// wrote them.
    /// </summary>
    /// <remarks>
    /// The stock verse is mild and clean, so an unexplained fallback is indistinguishable from the
    /// AI simply refusing to be funny — which is exactly how a content-filter rejection used to
    /// present. Same rule as <see cref="DescriptionFallbackReason"/>: name the degradation.
    /// </remarks>
    public string? LyricsFallbackReason { get; set; }

    /// <summary>The image description the lyrics were written from — shown as "what the AI saw".</summary>
    public string ImageDescription { get; set; } = string.Empty;

    /// <summary>
    /// True when a vision model produced <see cref="ImageDescription"/>. False means it fell back to
    /// the vision backend's tag-derived text, which yields noticeably blander lyrics — surfaced in
    /// the UI so a generic roast is explainable rather than mysterious.
    /// </summary>
    public bool DescriptionIsDetailed { get; set; }

    /// <summary>
    /// Why the detailed read did not happen, when <see cref="DescriptionIsDetailed"/> is false.
    /// Null when the description is detailed.
    /// </summary>
    /// <remarks>
    /// Exists because the UI previously hardcoded a single explanation ("no vision model was
    /// available — configure HuggingFace:ApiKey") for every fallback path. A rate-limited call, a
    /// content-filter refusal, and an unconfigured backend are different problems with different
    /// recoveries, and naming the wrong one sent people to change settings that were already fine.
    /// </remarks>
    public string? DescriptionFallbackReason { get; set; }

    /// <summary>
    /// Every provider gate this run passed through and what each one did with it.
    /// </summary>
    public FilterReportDto FilterReport { get; set; } = new();

    /// <summary>Structured scene slots, when a vision model produced them. Null otherwise.</summary>
    public SceneSnapshotDto? Scene { get; set; }

    public long TotalMs { get; set; }
}

/// <summary>
/// What the safety filters did to this run, gate by gate.
/// </summary>
/// <remarks>
/// Two independent filters sit in the roast pipeline — the lyric model's (Azure OpenAI) and the
/// music model's (Lyria) — and at the top of the intensity dial either can bounce a draft. Without
/// this the two are indistinguishable from the outside: a filtered verse and a model that simply
/// wrote something tame look identical on the page, which is exactly how a clean run got mistaken
/// for the AI going soft. The counts are per run, so with at most two attempts at each gate the
/// percentage is coarse by nature — it is a "did the filters bite, and where" readout, not a
/// statistic. A rate worth trusting comes from the client tallying these across a session.
/// </remarks>
public class FilterReportDto
{
    /// <summary>One entry per provider call, in the order they happened.</summary>
    public IReadOnlyList<FilterAttemptDto> Attempts { get; set; } = [];

    /// <summary>Total provider calls made across both gates.</summary>
    public int TotalAttempts { get; set; }

    /// <summary>How many of those a safety filter rejected.</summary>
    public int RejectedAttempts { get; set; }

    /// <summary>
    /// <see cref="RejectedAttempts"/> as a whole-number percentage of <see cref="TotalAttempts"/>.
    /// Zero when nothing ran, which reads correctly as "nothing was filtered".
    /// </summary>
    public int RejectedPercent { get; set; }
}

/// <summary>One provider call and its verdict.</summary>
public class FilterAttemptDto
{
    /// <summary>Display name of the gate, e.g. "Lyric model" or "Music model".</summary>
    public string Gate { get; set; } = string.Empty;

    /// <summary>Which provider backs this gate, e.g. "Azure OpenAI" / "Lyria".</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>1-based attempt number within that gate.</summary>
    public int Attempt { get; set; }

    /// <summary>True when a safety filter refused this call.</summary>
    public bool Rejected { get; set; }

    /// <summary>Provider-supplied explanation, when there was one.</summary>
    public string? Reason { get; set; }
}

/// <summary>
/// Wire shape of the structured scene read. Mirrors the Application-layer snapshot; Shared cannot
/// reference Application, so the endpoint maps between them.
/// </summary>
public class SceneSnapshotDto
{
    public IReadOnlyList<string> Outfit { get; set; } = [];
    public string? Pose { get; set; }
    public string? Expression { get; set; }
    public string? Setting { get; set; }
    public IReadOnlyList<string> Props { get; set; } = [];

    /// <summary>Text read from the image by OCR — exact, not inferred.</summary>
    public IReadOnlyList<string> TextInImage { get; set; } = [];

    public string? MostIncongruousDetail { get; set; }
}
