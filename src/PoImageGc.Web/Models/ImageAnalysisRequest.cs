using System.ComponentModel.DataAnnotations;

namespace PoImageGc.Web.Models;

/// <summary>
/// Represents an image analysis request sent from client to server.
/// Uses the Data Transfer Object (DTO) pattern to decouple API contract from internal domain.
/// </summary>
public class ImageAnalysisRequest
{
    /// <summary>
    /// Gets or sets the base64-encoded image data
    /// </summary>
    [Required]
    public string ImageData { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the MIME type of the image (e.g., image/jpeg, image/png)
    /// </summary>
    [Required]
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the original filename
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the desired length of the generated description in words
    /// </summary>
    [Range(200, 500)]
    public int DescriptionLength { get; set; } = 200;

    /// <summary>
    /// Gets or sets the processing mode (ImageRegeneration or MemeGeneration)
    /// </summary>
    public ProcessingMode Mode { get; set; } = ProcessingMode.ImageRegeneration;
}
