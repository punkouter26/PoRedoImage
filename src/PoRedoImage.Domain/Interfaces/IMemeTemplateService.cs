using PoRedoImage.Domain.Entities;

namespace PoRedoImage.Domain.Interfaces;

/// <summary>
/// Domain service for the Meme Template Library (Idea #17).
/// Returns the curated list of meme layouts and renders text into a
/// template's predefined text zones.
/// </summary>
public interface IMemeTemplateService
{
    /// <summary>Returns the full catalog of available templates.</summary>
    IReadOnlyList<MemeTemplate> GetTemplates();

    /// <summary>Looks up a single template by id; null if not found.</summary>
    MemeTemplate? GetById(string id);

    /// <summary>
    /// Renders the supplied text strings into the template's zones and overlays them
    /// on the source image. Returns the encoded image bytes.
    /// </summary>
    /// <param name="sourceImage">Raw JPEG/PNG bytes of the user's photo.</param>
    /// <param name="template">Template describing where to place the text.</param>
    /// <param name="zoneTexts">One text per zone, in zone order. Empty/whitespace entries are skipped.</param>
    Task<(byte[] ImageData, string ContentType)> RenderAsync(
        byte[] sourceImage,
        MemeTemplate template,
        IReadOnlyList<string> zoneTexts,
        CancellationToken ct = default);
}
