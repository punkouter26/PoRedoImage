namespace PoRedoImage.Shared.DTOs;

/// <summary>
/// Response DTO returned from the API to the Blazor WASM client.
/// </summary>
public class ImageAnalysisResponse
{
    public string Description { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public double ConfidenceScore { get; set; }
    public string? RegeneratedImageData { get; set; }
    public string RegeneratedImageContentType { get; set; } = "image/png";
    public ProcessingMetricsDto Metrics { get; set; } = new();
    public string? MemeImageData { get; set; }
    public string? MemeCaption { get; set; }

    /// <summary>
    /// Set when the description did not come from a model that actually looked at the photo —
    /// e.g. Azure Computer Vision's Caption feature is unavailable in the configured region and the
    /// text was synthesised from detected tags instead. Null on the normal path.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>RapRoastResponse.DescriptionFallbackReason</c> and
    /// <c>StyleDirectorResponse.FallbackReason</c>. Image analysis is the app's primary flow and was
    /// the one degrading silently: on a region without Caption support every single request returned
    /// tag-derived text with no indication the photo was never described, which reads to the user as
    /// "the AI ignored my photo".
    /// </remarks>
    public string? DescriptionFallbackReason { get; set; }
}

/// <summary>
/// Processing metrics DTO for telemetry surface in the UI.
/// </summary>
public class ProcessingMetricsDto
{
    public long ImageAnalysisTimeMs { get; set; }
    public long DescriptionGenerationTimeMs { get; set; }
    public long ImageRegenerationTimeMs { get; set; }
    public int DescriptionTokensUsed { get; set; }
    public string? ErrorInfo { get; set; }
    public long TotalProcessingTimeMs => ImageAnalysisTimeMs + DescriptionGenerationTimeMs + ImageRegenerationTimeMs;
}
