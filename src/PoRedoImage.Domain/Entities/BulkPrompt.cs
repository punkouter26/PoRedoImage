namespace PoRedoImage.Domain.Entities;

/// <summary>
/// Domain entity representing a stored bulk-generate prompt in Table Storage.
/// Follows the Entity pattern — row key is the natural identity.
/// </summary>
public sealed class BulkPrompt
{
    public string PartitionKey { get; init; } = "prompts";
    public string RowKey { get; init; } = Guid.NewGuid().ToString();
    public string Name { get; init; } = string.Empty;
    public string PromptText { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public static BulkPrompt Create(string rowKey, string name, string promptText) =>
        new() { RowKey = rowKey, Name = name, PromptText = promptText };
}
