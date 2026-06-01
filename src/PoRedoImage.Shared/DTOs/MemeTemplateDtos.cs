using PoRedoImage.Domain.Entities;

namespace PoRedoImage.Shared.DTOs;

/// <summary>
/// Wire DTO for a meme template — serialises only what the client needs to render the picker UI.
/// The full MemeTemplate domain type also serialises the same fields; this DTO is the contract.
/// </summary>
public record MemeTemplateDto(
    string Id,
    string Name,
    string Description,
    string Category,
    int RequiredZoneCount,
    IReadOnlyList<MemeTextZoneDto> Zones);

public record MemeTextZoneDto(
    string Label,
    double X,
    double Y,
    double MaxWidthRatio,
    double FontSizeRatio,
    string Alignment);

/// <summary>
/// Request to render a meme using a template (Idea #17).
/// </summary>
public record MemeTemplateRenderRequest(
    string ImageData,
    string ContentType,
    string TemplateId,
    IReadOnlyList<string> ZoneTexts);

/// <summary>
/// Response — base64 PNG + AI-free metrics.
/// </summary>
public record MemeTemplateRenderResponse(
    string ImageData,
    string ContentType,
    string TemplateId,
    int RenderedZones,
    long ElapsedMs);

public static class MemeTemplateMappingExtensions
{
    public static MemeTemplateDto ToDto(this MemeTemplate template) => new(
        template.Id,
        template.Name,
        template.Description,
        template.Category,
        template.RequiredZoneCount,
        template.Zones.Select(z => new MemeTextZoneDto(
            z.Label, z.X, z.Y, z.MaxWidthRatio, z.FontSizeRatio, z.Alignment)).ToList());

    public static IReadOnlyList<MemeTemplateDto> ToDtos(this IEnumerable<MemeTemplate> templates) =>
        templates.Select(t => t.ToDto()).ToList();
}
