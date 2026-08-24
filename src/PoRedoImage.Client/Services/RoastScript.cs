namespace PoRedoImage.Client.Services;

/// <summary>
/// One line of a parsed roast, with the slice of the track it is estimated to occupy.
/// </summary>
/// <param name="Index">Position in the parsed list — the key the JS karaoke driver highlights by.</param>
/// <param name="Text">The line as written. Section markers keep their brackets.</param>
/// <param name="IsSection">True for <c>[Verse]</c> / <c>[Chorus]</c> markers, which are never sung.</param>
/// <param name="StartFraction">Estimated start, as a fraction of total track duration (0–1).</param>
/// <param name="EndFraction">Estimated end. Equal to <paramref name="StartFraction"/> for a section marker.</param>
public sealed record RoastLine(
    int Index,
    string Text,
    bool IsSection,
    double StartFraction,
    double EndFraction);

/// <summary>
/// Turns section-tagged roast lyrics into per-line timings for the karaoke view.
/// </summary>
/// <remarks>
/// <para>
/// <b>These timings are an estimate and the UI says so.</b> Lyria performs the lyrics but returns
/// only audio — no word or line alignment comes back with it, and running a forced aligner in the
/// browser to recover one would cost more than the feature is worth. So each sung line is given a
/// share of the track proportional to its length, which tracks a rapped delivery well enough to
/// follow along and drifts on a line the performer stretches or swallows. The page pairs this with a
/// manual sync nudge rather than pretending the estimate is measurement.
/// </para>
/// <para>
/// Length is measured in characters, not syllables. A syllable counter is a pile of English
/// heuristics that would be wrong often enough to not repay its own code — and the two agree closely
/// on rap lines, which are written to a near-constant syllable density by construction.
/// </para>
/// </remarks>
public static class RoastScript
{
    /// <summary>Share of the clip assumed to be beat before the first vocal lands.</summary>
    /// <remarks>
    /// Every generated track opens on some amount of instrumental. Six percent is a deliberately
    /// small allowance: guessing short leaves the first line highlighted slightly early, which reads
    /// as anticipation, while guessing long leaves it highlighted late, which reads as broken.
    /// </remarks>
    internal const double LeadInFraction = 0.06;

    /// <summary>Share of the clip assumed to be outro after the last bar.</summary>
    internal const double TailFraction = 0.04;

    /// <summary>
    /// Floor on a line's length weight, so a two-word ad-lib still gets a readable moment on screen
    /// instead of flashing past.
    /// </summary>
    private const int MinimumWeight = 8;

    /// <summary>
    /// Parses <paramref name="lyrics"/> into ordered lines with estimated timings. Blank lines are
    /// dropped; section markers are kept (they anchor the scroll) but given zero duration.
    /// Returns an empty list when there is nothing to sing.
    /// </summary>
    public static IReadOnlyList<RoastLine> Parse(string? lyrics)
    {
        if (string.IsNullOrWhiteSpace(lyrics)) return [];

        var raw = lyrics
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (raw.Length == 0) return [];

        var totalWeight = raw.Where(l => !IsSectionMarker(l)).Sum(Weight);

        // Nothing but section markers — no timeline to build, so the caller falls back to the
        // static lyric block rather than showing a karaoke view that can never advance.
        if (totalWeight == 0) return [];

        var window = 1.0 - LeadInFraction - TailFraction;
        var cursor = LeadInFraction;
        var lines = new List<RoastLine>(raw.Length);

        for (var i = 0; i < raw.Length; i++)
        {
            var text = raw[i];
            if (IsSectionMarker(text))
            {
                // Zero-length and sitting exactly on the boundary: a marker is a place in the
                // script, not a moment in the track.
                lines.Add(new RoastLine(i, text, IsSection: true, cursor, cursor));
                continue;
            }

            var start = cursor;
            cursor += window * Weight(text) / totalWeight;
            lines.Add(new RoastLine(i, text, IsSection: false, start, cursor));
        }

        return lines;
    }

    /// <summary>A marker is a whole line wrapped in brackets, e.g. <c>[Chorus]</c>.</summary>
    private static bool IsSectionMarker(string line) =>
        line.Length > 1 && line[0] == '[' && line[^1] == ']';

    private static int Weight(string line) => Math.Max(MinimumWeight, line.Length);
}
