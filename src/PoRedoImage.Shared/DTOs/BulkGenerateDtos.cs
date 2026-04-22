namespace PoRedoImage.Shared.DTOs;

public class BulkGenerateImageResult
{
    public int Index { get; set; }
    public BulkGenerateStatus Status { get; set; } = BulkGenerateStatus.Pending;
    public string? ImageUrl { get; set; }
    public string? Prompt { get; set; }
    public string? ErrorMessage { get; set; }
}

public enum BulkGenerateStatus
{
    Pending,
    Processing,
    Complete,
    Failed
}

public class BulkPromptDto
{
    public string RowKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PromptText { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public record BulkDescribeRequest(string ImageData, string ContentType);
public record BulkDescribeResponse(string Description);

public record BulkVariationRequest(string ImageData, string ContentType, string Prompt);
public record BulkVariationResponse(string ImageData, string ContentType);
