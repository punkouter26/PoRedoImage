namespace PoImageGc.Web.Models;

/// <summary>
/// Represents the analysis results returned from the server after processing an image.
/// DTO pattern: carries data between the API layer and client without business logic.
/// </summary>
public class ImageAnalysisResult
{
    /// <summary>
    /// Gets or sets the detailed description of the image
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the tags identified in the image
    /// </summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Gets or sets the confidence score (0.0 to 1.0) for the image analysis
    /// </summary>
    public double ConfidenceScore { get; set; }

    /// <summary>
    /// Gets or sets the base64-encoded regenerated image data
    /// </summary>
    public string? RegeneratedImageData { get; set; }

    /// <summary>
    /// Gets or sets the content type of the regenerated image
    /// </summary>
    public string RegeneratedImageContentType { get; set; } = "image/png";

    /// <summary>
    /// Gets or sets the processing metrics for telemetry and diagnostics
    /// </summary>
    public ProcessingMetrics Metrics { get; set; } = new();

    /// <summary>
    /// Gets or sets the base64-encoded meme image data (when Mode = MemeGeneration)
    /// </summary>
    public string? MemeImageData { get; set; }

    /// <summary>
    /// Gets or sets the AI-generated meme caption text (when Mode = MemeGeneration)
    /// </summary>
    public string? MemeCaption { get; set; }
}
