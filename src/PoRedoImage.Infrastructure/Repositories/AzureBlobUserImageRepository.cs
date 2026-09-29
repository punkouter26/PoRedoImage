using Azure;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Entities;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Shared.DTOs;
using PoRedoImage.Shared.Configuration;

namespace PoRedoImage.Infrastructure.Repositories;

/// <summary>
/// User image repository backed by Azure Blob Storage (bytes) and Azure Table Storage (metadata).
/// Both services share the same Storage connection string as BulkPromptRepository.
/// Blob container: "user-images" — blobs named "{userId}/{imageId}".
/// Table: "UserImages" — PartitionKey = userId, RowKey = imageId, tags in a Tags column.
/// </summary>
public sealed class AzureBlobUserImageRepository : IUserImageRepository
{
    private const string ContainerName = "user-images";
    private const string TableName = "UserImages";

    private readonly BlobContainerClient? _blobContainer;
    private readonly TableClient? _tableClient;
    private readonly ILogger<AzureBlobUserImageRepository> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public AzureBlobUserImageRepository(IConfiguration configuration, ILogger<AzureBlobUserImageRepository> logger)
    {
        _logger = logger;
        var connectionString = configuration[ConfigKeys.StorageConnectionString];
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            _blobContainer = new BlobServiceClient(connectionString).GetBlobContainerClient(ContainerName);
            _tableClient = new TableServiceClient(connectionString).GetTableClient(TableName);
        }
        else
        {
            _logger.LogWarning("Storage:ConnectionString not configured; user image gallery is disabled.");
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;
        await _initLock.WaitAsync(ct);
        try
        {
            if (!_initialized)
            {
                if (_blobContainer is not null)
                    await _blobContainer.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
                if (_tableClient is not null)
                    await _tableClient.CreateIfNotExistsAsync(ct);
                _initialized = true;
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<string> SaveBlobAsync(string userId, UserImageId imageId, byte[] bytes, string contentType, CancellationToken ct = default)
    {
        if (_blobContainer is null) return string.Empty;
        await EnsureInitializedAsync(ct);

        var blobName = $"{userId}/{imageId}";
        var blobClient = _blobContainer.GetBlobClient(blobName);
        using var stream = new MemoryStream(bytes, writable: false);
        var headers = new BlobHttpHeaders { ContentType = contentType };
        await blobClient.UploadAsync(stream, new BlobUploadOptions { HttpHeaders = headers }, cancellationToken: ct);

        _logger.LogInformation("Saved user image blob {BlobName} ({Bytes} bytes)", blobName, bytes.Length);
        return blobClient.Uri.ToString();
    }

    /// <summary>
    /// Tags are stored comma-joined in one table column. Commas inside a tag become ';' so the
    /// join stays reversible — the same encoding the older blob-metadata rows used.
    /// </summary>
    private static string JoinTags(IReadOnlyList<string>? tags) =>
        tags is null ? string.Empty : string.Join(',', tags
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Replace(',', ';').Trim()));

    private static IReadOnlyList<string> ParseTags(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .Select(t => t.Replace(';', ','))
                 .ToList()
                 .AsReadOnly();

    public async Task SaveMetadataAsync(UserImage image, CancellationToken ct = default)
    {
        if (_tableClient is null) return;
        await EnsureInitializedAsync(ct);

        var entity = new UserImageTableEntity
        {
            PartitionKey = image.UserId,
            RowKey = image.Id.Value,
            FileName = image.FileName,
            ContentType = image.ContentType,
            Kind = image.Kind.ToString(),
            CreatedAt = image.CreatedAt,
            SizeBytes = image.SizeBytes,
            Tags = JoinTags(image.Tags)
        };

        await _tableClient.UpsertEntityAsync(entity, cancellationToken: ct);
        _logger.LogInformation("Saved user image metadata {Id} kind={Kind}", image.Id, image.Kind);
    }

    /// <remarks>
    /// Storage failures propagate: the endpoint turns them into a 503 the gallery shows with a
    /// Retry. Returning an empty list here made a storage outage read as "all your images are gone".
    /// </remarks>
    public async Task<IReadOnlyList<UserImage>> GetByUserAsync(string userId, CancellationToken ct = default)
    {
        if (_tableClient is null) return [];
        await EnsureInitializedAsync(ct);

        // ponytail: whole partition in memory (metadata only, ~200 bytes/row); page server-side
        // with a reverse-ticks RowKey if a user ever holds tens of thousands of images.
        var entities = new List<UserImageTableEntity>();
        await foreach (var entity in _tableClient.QueryAsync<UserImageTableEntity>(
            filter: TableClient.CreateQueryFilter($"PartitionKey eq {userId}"), cancellationToken: ct))
        {
            entities.Add(entity);
        }

        // Rows written before tags moved into the table carry Tags == null. Read those once from
        // the blob's metadata and write them back, so each legacy row costs one HEAD ever rather
        // than one HEAD per gallery load.
        await Parallel.ForEachAsync(
            entities.Where(e => e.Tags is null),
            new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct },
            async (entity, token) => await BackfillLegacyTagsAsync(entity, token));

        return entities.Select(MapToDomain).OrderByDescending(i => i.CreatedAt).ToList().AsReadOnly();
    }

    private async Task BackfillLegacyTagsAsync(UserImageTableEntity entity, CancellationToken ct)
    {
        if (_blobContainer is null || _tableClient is null) return;
        try
        {
            var blobClient = _blobContainer.GetBlobClient($"{entity.PartitionKey}/{entity.RowKey}");
            string tags;
            try
            {
                var props = await blobClient.GetPropertiesAsync(cancellationToken: ct);
                tags = props.Value.Metadata.TryGetValue("Tags", out var raw) ? raw : string.Empty;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                tags = string.Empty;
            }

            entity.Tags = tags;
            await _tableClient.UpdateEntityAsync(
                new TableEntity(entity.PartitionKey, entity.RowKey) { [nameof(UserImageTableEntity.Tags)] = tags },
                ETag.All, TableUpdateMode.Merge, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Tags are decoration; a failed backfill shows the image untagged and retries next load.
            _logger.LogWarning(ex, "Tag backfill failed for {UserId}/{ImageId}", entity.PartitionKey, entity.RowKey);
        }
    }

    public async Task<(byte[] Bytes, string ContentType)?> GetBlobAsync(string userId, UserImageId imageId, CancellationToken ct = default)
    {
        if (_blobContainer is null) return null;
        try
        {
            await EnsureInitializedAsync(ct);
            var blobClient = _blobContainer.GetBlobClient($"{userId}/{imageId}");
            var response = await blobClient.DownloadContentAsync(ct);
            var props = await blobClient.GetPropertiesAsync(cancellationToken: ct);
            return (response.Value.Content.ToArray(), props.Value.ContentType);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve user image blob {UserId}/{ImageId}", userId, imageId);
            return null;
        }
    }

    public async Task<UserImage?> GetMetadataAsync(string userId, UserImageId imageId, CancellationToken ct = default)
    {
        if (_tableClient is null) return null;
        await EnsureInitializedAsync(ct);

        try
        {
            var entity = await _tableClient.GetEntityAsync<UserImageTableEntity>(userId, imageId.Value, cancellationToken: ct);
            return MapToDomain(entity.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string userId, UserImageId imageId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        if (_blobContainer is not null)
        {
            var blobClient = _blobContainer.GetBlobClient($"{userId}/{imageId}");
            await blobClient.DeleteIfExistsAsync(cancellationToken: ct);
        }

        if (_tableClient is not null)
        {
            try
            {
                await _tableClient.DeleteEntityAsync(userId, imageId.Value, cancellationToken: ct);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // Already removed — treat as success
            }
        }

        _logger.LogInformation("Deleted user image {UserId}/{ImageId}", userId, imageId);
    }

    private static UserImage MapToDomain(UserImageTableEntity entity) => new()
    {
        UserId = entity.PartitionKey,
        Id = UserImageId.Parse(entity.RowKey),
        FileName = entity.FileName,
        ContentType = entity.ContentType,
        Kind = Enum.TryParse<UserImageKind>(entity.Kind, out var k) ? k : UserImageKind.Original,
        CreatedAt = entity.CreatedAt,
        SizeBytes = entity.SizeBytes,
        Tags = ParseTags(entity.Tags)
    };
}

internal sealed class UserImageTableEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public Azure.ETag ETag { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "image/jpeg";
    public string Kind { get; set; } = "Original";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public long SizeBytes { get; set; }

    /// <summary>Comma-joined tags; null only on rows written before this column existed.</summary>
    public string? Tags { get; set; }
}
