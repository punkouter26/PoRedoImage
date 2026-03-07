namespace PoRedoImage.Web.Models;

public enum BulkGenerateStatus
{
    Pending,
    Processing,
    Complete,
    Failed
}

public class BulkGenerateImageResult
{
    public int Index { get; set; }
    public BulkGenerateStatus Status { get; set; } = BulkGenerateStatus.Pending;
    public string? ImageUrl { get; set; }
    public string? Prompt { get; set; }
    public string? ErrorMessage { get; set; }
}
