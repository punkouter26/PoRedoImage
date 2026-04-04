using System.ComponentModel.DataAnnotations;

namespace PoShared.Models;

/// <summary>
/// Shared image analysis request DTO — used by both the server API and any future WASM client.
/// Mirrors PoRedoImage.Web.Models.ImageAnalysisRequest for cross-project portability.
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

/// <summary>
/// Shared analysis result DTO.
/// </summary>
public class ImageAnalysisResult
{
    public string Description { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public double ConfidenceScore { get; set; }
    public string? RegeneratedImageData { get; set; }
    public string RegeneratedImageContentType { get; set; } = "image/png";
    public ProcessingMetrics Metrics { get; set; } = new();
    public string? MemeImageData { get; set; }
    public string? MemeCaption { get; set; }
}

/// <summary>
/// Shared processing metrics.
/// </summary>
public class ProcessingMetrics
{
    public long ImageAnalysisTimeMs { get; set; }
    public long DescriptionGenerationTimeMs { get; set; }
    public long ImageRegenerationTimeMs { get; set; }
    public int DescriptionTokensUsed { get; set; }
    public string? ErrorDetails { get; set; }
    public long TotalElapsedMs => ImageAnalysisTimeMs + DescriptionGenerationTimeMs + ImageRegenerationTimeMs;
}

/// <summary>
/// Shared processing mode enum.
/// </summary>
public enum ProcessingMode
{
    ImageRegeneration = 0,
    MemeGeneration = 1
}

/// <summary>
/// Shared bulk generate models.
/// </summary>
public enum BulkGenerateStatus { Pending, Processing, Complete, Failed }

public class BulkGenerateImageResult
{
    public int Index { get; set; }
    public BulkGenerateStatus Status { get; set; } = BulkGenerateStatus.Pending;
    public string? ImageUrl { get; set; }
    public string? Prompt { get; set; }
    public string? ErrorMessage { get; set; }
}
