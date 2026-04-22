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
}
