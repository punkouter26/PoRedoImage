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
