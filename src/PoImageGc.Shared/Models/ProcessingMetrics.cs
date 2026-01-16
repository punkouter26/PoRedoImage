namespace PoImageGc.Shared.Models;

/// <summary>
/// Represents metrics collected during image processing for performance tracking and diagnostics
/// </summary>
public class ProcessingMetrics
{
    /// <summary>
    /// Gets or sets the time taken (in milliseconds) for the image analysis phase
    /// </summary>
    public long ImageAnalysisTimeMs { get; set; }

    /// <summary>
    /// Gets or sets the time taken (in milliseconds) for the description generation phase
    /// </summary>
    public long DescriptionGenerationTimeMs { get; set; }

    /// <summary>
    /// Gets or sets the time taken (in milliseconds) for the image regeneration phase
    /// </summary>
    public long ImageRegenerationTimeMs { get; set; }

    /// <summary>
    /// Gets or sets the number of tokens used for description generation
    /// </summary>
    public int DescriptionTokensUsed { get; set; }

    /// <summary>
    /// Gets or sets the number of tokens used for image regeneration
    /// </summary>
    public int RegenerationTokensUsed { get; set; }

    /// <summary>
    /// Gets or sets any error information that occurred during processing
    /// </summary>
    public string? ErrorInfo { get; set; }

    /// <summary>
    /// Gets the total processing time in milliseconds
    /// </summary>
    public long TotalProcessingTimeMs => ImageAnalysisTimeMs + DescriptionGenerationTimeMs + ImageRegenerationTimeMs;
}
