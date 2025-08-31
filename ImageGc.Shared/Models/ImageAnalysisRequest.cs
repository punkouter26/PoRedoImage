using System.ComponentModel.DataAnnotations;

namespace ImageGc.Shared.Models;

/// <summary>
/// Represents an image analysis request sent from client to server
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
}