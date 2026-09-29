using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PoRedoImage.Domain.Entities;
using PoRedoImage.Infrastructure.Repositories;
using PoRedoImage.Shared.Configuration;
using PoRedoImage.Shared.DTOs;

namespace PoRedoImage.Tests.Integration;

/// <summary>
/// The gallery list against real Azurite: tags come from the table (no per-image blob HEAD),
/// rows written before that column existed are backfilled from blob metadata, and the user id
/// reaches the OData filter escaped.
/// </summary>
[Collection(AzuriteCollection.Name)]
public sealed class UserImageGalleryStorageTests(AzuriteContainerFixture azurite)
{
    [DockerFact]
    public async Task Gallery_reads_table_tags_backfills_legacy_rows_and_escapes_the_user_id()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [ConfigKeys.StorageConnectionString] = azurite.ConnectionString })
            .Build();
        var repo = new AzureBlobUserImageRepository(config, NullLogger<AzureBlobUserImageRepository>.Instance);
        // The quote used to be spliced raw into "PartitionKey eq '...'", breaking the query.
        var userId = $"o'brien-{Guid.NewGuid():N}";

        var fresh = UserImage.Create(userId, "a.png", "image/png", UserImageKind.Meme, 3, ["cat", "hat, red"]);
        await repo.SaveBlobAsync(userId, fresh.Id, [1, 2, 3], "image/png");
        await repo.SaveMetadataAsync(fresh);

        // Legacy row: no Tags column; its tags live only on the blob's metadata.
        var legacyId = UserImageId.New();
        await new BlobContainerClient(azurite.ConnectionString, "user-images")
            .GetBlobClient($"{userId}/{legacyId}")
            .UploadAsync(BinaryData.FromBytes([4]), new BlobUploadOptions { Metadata = new Dictionary<string, string> { ["Tags"] = "dog,park" } });
        var table = new TableClient(azurite.ConnectionString, "UserImages");
        await table.AddEntityAsync(new TableEntity(userId, legacyId.Value)
        {
            ["FileName"] = "old.png",
            ["ContentType"] = "image/png",
            ["Kind"] = "Original",
            ["CreatedAt"] = DateTimeOffset.UtcNow.AddDays(-1),
            ["SizeBytes"] = 1L,
        });

        var gallery = await repo.GetByUserAsync(userId);

        Assert.Equal([fresh.Id, legacyId], gallery.Select(i => i.Id));   // newest first
        Assert.Equal(["cat", "hat, red"], gallery[0].Tags);
        Assert.Equal(["dog", "park"], gallery[1].Tags);
        var backfilled = await table.GetEntityAsync<TableEntity>(userId, legacyId.Value);
        Assert.Equal("dog,park", backfilled.Value.GetString("Tags"));
    }
}
