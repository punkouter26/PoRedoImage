using System.ComponentModel.DataAnnotations;
using PoRedoImage.Shared.Validation;

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
    /// <remarks>
    /// This is client-supplied free text that flows verbatim into <c>EnhanceDescriptionAsync</c> /
    /// <c>GenerateMemeCaptionAsync</c> as prompt tokens on a metered model. The endpoint is
    /// authenticated and rate-limited, but a field that becomes billable tokens should still carry a
    /// cap rather than being unbounded.
    /// </remarks>
    [StringLength(4000)]
    public string? PrecomputedDescription { get; set; }

    /// <summary>
    /// Tags accompanying <see cref="PrecomputedDescription"/>. Ignored unless that is also set.
    /// </summary>
    /// <remarks>
    /// The client's <c>FeaturePageBase.ExtractTags</c> (the only real producer) caps at 10, so 20
    /// leaves headroom without being unbounded; <see cref="MaxItemLengthAttribute"/> bounds each
    /// entry's length since <see cref="MaxCountAttribute"/> only caps the list's count.
    /// </remarks>
    [MaxCount(20)]
    [MaxItemLength(100)]
    public IReadOnlyList<string>? PrecomputedTags { get; set; }
}
