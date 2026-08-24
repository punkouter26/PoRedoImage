namespace PoRedoImage.Client.Shared;

/// <summary>One transformation the Studio offers, as shown on a board row and in cross-feature links.</summary>
/// <param name="Route">The page's canonical <c>@page</c> route.</param>
/// <param name="Icon">
/// Bootstrap-icon class. This is the only glyph a feature has: the emoji field that used to sit
/// beside it was removed because emoji are not an icon system — they render in whatever colour and
/// weight the platform font decides, which is exactly what a board built on one consistent stroke
/// cannot absorb. Every surface now draws from this single library at one size and weight.
/// </param>
public sealed record FeatureLink(
    string Route,
    string Title,
    string Icon,
    string Description);

/// <summary>
/// The single list of user-facing transformations. Studio renders its cards from this, the results
/// panels build their "Send result to…" row from it, and "Surprise me" picks from it — so adding a
/// feature page means adding one entry here rather than editing three call sites.
/// </summary>
public static class FeatureCatalog
{
    public static readonly IReadOnlyList<FeatureLink> All =
    [
        new("/image-regeneration", "Regeneration", "bi-palette2",
            "AI recreates your photo as a brand-new artistic version using Gemini."),
        new("/meme-generation", "Meme", "bi-chat-square-text",
            "AI writes a witty caption and overlays it on your image."),
        new("/bulk-generate", "Bulk Generate", "bi-grid-3x3",
            "Generate up to 10 artistic variations at once via Gemini 2.0 Flash."),
        new("/rap-roast", "Rap Roast", "bi-mic",
            "AI writes a roast verse about your photo, then performs it over a beat."),
        new("/style-director", "Style Director", "bi-sliders",
            "AI synthesizes the optimal art-style direction and refined prompt, then Gemini paints it."),
    ];

    /// <summary>Every feature except the one at <paramref name="route"/> (case-insensitive).</summary>
    public static IEnumerable<FeatureLink> Except(string? route) =>
        All.Where(f => !string.Equals(f.Route, route, StringComparison.OrdinalIgnoreCase));
}
