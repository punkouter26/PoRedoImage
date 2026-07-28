using PoRedoImage.Domain.Entities;

namespace PoRedoImage.Tests.Unit.Features.Slice;

/// <summary>
/// Slice test for the <see cref="BulkPrompt"/> factory's default-partition contract.
/// </summary>
public class BulkPromptTableEntityRoundTripTests
{
    [Fact]
    public void BulkPrompt_Create_Factory_SetsDefaultPartition()
    {
        var prompt = BulkPrompt.Create("user-x", "user-x", "[]");
        Assert.Equal("prompts", prompt.PartitionKey);  // The default partition matches the table name.
        Assert.Equal("user-x", prompt.RowKey);
        Assert.False(string.IsNullOrEmpty(prompt.CreatedAt.ToString("O")));
    }
}
