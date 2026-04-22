namespace PoRedoImage.Shared.DTOs;

public class BulkGenerateRequest
{
    public List<string> Prompts { get; set; } = [];
    public int Count { get; set; } = 1;
}

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
