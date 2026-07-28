using System.ComponentModel.DataAnnotations;

namespace PoRedoImage.Shared.DTOs;

/// <summary>
/// Request DTO sent from Blazor WASM client to the API server for image analysis.
/// DTO pattern: decouples API contract from domain model.
/// </summary>
public class ImageAnalysisRequest
{
    [Required]
    public string ImageData { get; set; } = string.Empty;

    [Required]
    public string ContentType { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    [Range(200, 500)]
    public int DescriptionLength { get; set; } = 200;

    public ProcessingMode Mode { get; set; } = ProcessingMode.ImageRegeneration;

    /// <summary>
    /// Optional selected vision provider id (see <c>AiProviderIds</c>, e.g. "ollama:vision").
    /// Null or unrecognised ids fall back to the default Azure vision service.
    /// </summary>
    public string? ModelId { get; set; }

    /// <summary>
    /// Optional selected image-generation provider id (see <c>AiProviderIds</c>). Null falls back to
    /// the provider named by the <c>ImageGen:Provider</c> flag.
    /// </summary>
    public string? ImageGenModelId { get; set; }

    /// <summary>
    /// Description already produced by a browser-local vision model. When set, the server skips its
    /// own vision step and uses this instead.
    /// </summary>
    public string? PrecomputedDescription { get; set; }

    /// <summary>
    /// Tags accompanying <see cref="PrecomputedDescription"/>. Ignored unless that is also set.
    /// </summary>
    public IReadOnlyList<string>? PrecomputedTags { get; set; }
}
