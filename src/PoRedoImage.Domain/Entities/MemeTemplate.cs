namespace PoRedoImage.Domain.Entities;

/// <summary>
/// Definition of a single text zone within a meme template.
/// Coordinates are normalized 0..1 against the source image — they scale
/// to any image size, which is what makes a template "reusable" across photos.
/// </summary>
/// <remarks>
/// Idea #17 — Meme Template Library. The library uses normalized (0..1) coordinates
/// so the same template can be applied to portrait, landscape, or square photos
/// without distortion or pre-baked sizing.
/// </remarks>
public sealed record MemeTextZone(
    string Label,
    double X,            // 0..1 horizontal center of the text
    double Y,            // 0..1 vertical baseline of the text
    double MaxWidthRatio, // max text width as a fraction of image width (0..1)
    double FontSizeRatio, // font height as a fraction of image height (0..1)
    string Alignment);   // "center" | "left" | "right"

/// <summary>
/// A reusable meme layout — classic formats like Drake, Distracted Boyfriend, etc.
/// Templates do NOT include the meme artwork; they only define where the text
/// goes when the user picks a format. The user's photo is the artwork.
/// </summary>
public sealed record MemeTemplate(
    string Id,                  // stable identifier (kebab-case)
    string Name,                // display name (e.g., "Drake Hotline Bling")
    string Description,         // one-line help text
    string Category,            // "classic" | "reaction" | "office" | "wholesome" | "experimental"
    int RequiredZoneCount,      // how many text zones must be filled
    IReadOnlyList<MemeTextZone> Zones);
