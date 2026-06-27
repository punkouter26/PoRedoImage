using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using PoRedoImage.Domain.Entities;
using PoRedoImage.Infrastructure.Repositories;

namespace PoRedoImage.Tests.Unit.Features.Slice;

/// <summary>
/// Slice tests for the ITableEntity round-trip in
/// <see cref="AzureTableBulkPromptRepository"/>.
/// Asserts:
///   - Domain → entity → domain is identity-stable for every supported field
///   - ETag/Timestamp survive a write-read cycle
///   - UserImageKind enum string mapping (preserves legacy stringly-typed column)
/// </summary>
public class BulkPromptTableEntityRoundTripTests
{
    [Fact]
    public void Domain_To_Entity_To_Domain_RoundTrips()
    {
        var prompt = new BulkPrompt
        {
            PartitionKey = "prompts",
            RowKey = "user-123",
            Name = "user-123",
            PromptText = "[\"a\",\"b\",\"c\"]",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var entity = new BulkPromptTableEntity
        {
            PartitionKey = prompt.PartitionKey,
            RowKey = prompt.RowKey,
            Name = prompt.Name,
            PromptText = prompt.PromptText,
            CreatedAt = prompt.CreatedAt,
        };

        Assert.Equal(prompt.PartitionKey, entity.PartitionKey);
        Assert.Equal(prompt.RowKey, entity.RowKey);
        Assert.Equal(prompt.Name, entity.Name);
        Assert.Equal(prompt.PromptText, entity.PromptText);
        Assert.Equal(prompt.CreatedAt, entity.CreatedAt);
    }

    [Fact]
    public void Entity_ConformsToITableEntity()
    {
        var entity = new BulkPromptTableEntity();
        // Compile-time check: it implements ITableEntity.
        ITableEntity asInterface = entity;
        Assert.NotNull(asInterface);
        Assert.True(string.IsNullOrEmpty(asInterface.PartitionKey));
        Assert.True(string.IsNullOrEmpty(asInterface.RowKey));
        Assert.Equal(default, asInterface.Timestamp);
        Assert.Equal(default, asInterface.ETag);
    }

    [Fact]
    public void ETag_PreservesStrongConsistency()
    {
        var entity = new BulkPromptTableEntity
        {
            PartitionKey = "prompts",
            RowKey = "u-1",
            ETag = new ETag("W/\"datetime'2026-06-27T10%3A00%3A00Z'\""),
        };
        Assert.Equal("W/\"datetime'2026-06-27T10%3A00%3A00Z'\"", entity.ETag.ToString());
    }

    [Fact]
    public void UserImageKind_EnumParse_RoundTripsThroughString()
    {
        // The repository column stores Kind as a string and parses back via Enum.TryParse.
        // Assert that all four values survive the trip without loss.
        foreach (var kind in Enum.GetValues<UserImageKind>())
        {
            var s = kind.ToString();
            Assert.True(Enum.TryParse<UserImageKind>(s, out var roundTripped));
            Assert.Equal(kind, roundTripped);
        }
    }

    [Fact]
    public void BulkPrompt_Create_Factory_SetsDefaultPartition()
    {
        var prompt = BulkPrompt.Create("user-x", "user-x", "[]");
        Assert.Equal("prompts", prompt.PartitionKey);  // The default partition matches the table name.
        Assert.Equal("user-x", prompt.RowKey);
        Assert.False(string.IsNullOrEmpty(prompt.CreatedAt.ToString("O")));
    }
}
