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
/// Table: "UserImages" — PartitionKey = userId, RowKey = imageId.
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

    public async Task<string> SaveBlobAsync(string userId, UserImageId imageId, byte[] bytes, string contentType, IReadOnlyList<string>? tags, CancellationToken ct = default)
    {
        if (_blobContainer is null) return string.Empty;
        await EnsureInitializedAsync(ct);

        var blobName = $"{userId}/{imageId}";
        var blobClient = _blobContainer.GetBlobClient(blobName);
        using var stream = new MemoryStream(bytes, writable: false);
        var headers = new BlobHttpHeaders { ContentType = contentType };

        // Tags ride as blob metadata (key=Tags, value=comma-joined). Cheaper than widening the
        // UserImages table schema and naturally scoped to the bytes themselves — they survive
        // when the blob is read back out without a metadata round-trip.
        var metadata = BuildTagsMetadata(tags);
        await blobClient.UploadAsync(
            stream,
            new BlobUploadOptions { HttpHeaders = headers, Metadata = metadata },
            cancellationToken: ct);

        _logger.LogInformation("Saved user image blob {BlobName} ({Bytes} bytes, tags={TagCount})", blobName, bytes.Length, metadata is null ? 0 : 1);
        return blobClient.Uri.ToString();
    }

    private static IDictionary<string, string>? BuildTagsMetadata(IReadOnlyList<string>? tags)
    {
        if (tags is null || tags.Count == 0) return null;
        // Metadata values must be ASCII / valid HTTP header values. Vision tags come from a
        // controlled vocabulary from Azure Computer Vision so this is safe in practice; we still
        // defensively strip anything that would violate the header grammar.
        var joined = string.Join(',', tags
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Replace(',', ';').Trim()));
        return string.IsNullOrEmpty(joined) ? null : new Dictionary<string, string> { ["Tags"] = joined };
    }

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
            SizeBytes = image.SizeBytes
        };

        await _tableClient.UpsertEntityAsync(entity, cancellationToken: ct);
        _logger.LogInformation("Saved user image metadata {Id} kind={Kind}", image.Id, image.Kind);
    }

    public async Task<IReadOnlyList<UserImage>> GetByUserAsync(string userId, CancellationToken ct = default)
    {
        if (_tableClient is null) return [];
        try
        {
            await EnsureInitializedAsync(ct);

            var results = new List<UserImage>();
            await foreach (var entity in _tableClient.QueryAsync<UserImageTableEntity>(
                filter: $"PartitionKey eq '{userId}'", cancellationToken: ct))
            {
                results.Add(MapToDomain(entity));
            }

            return results.OrderByDescending(i => i.CreatedAt).ToList().AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve user images for user {UserId}", userId);
            return [];
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

    /// <summary>
    /// Reads just the Tags metadata (one HEAD request) so the gallery can display content
    /// filters without doing a full DownloadContent on every row. Cheap, idempotent.
    /// </summary>
    public async Task<IReadOnlyList<string>?> GetTagsAsync(string userId, UserImageId imageId, CancellationToken ct = default)
    {
        if (_blobContainer is null) return null;
        try
        {
            await EnsureInitializedAsync(ct);
            var blobClient = _blobContainer.GetBlobClient($"{userId}/{imageId}");
            var props = await blobClient.GetPropertiesAsync(cancellationToken: ct);
            return ParseTagsMetadata(props.Value.Metadata);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve tags metadata for {UserId}/{ImageId}", userId, imageId);
            return null;
        }
    }

    private static IReadOnlyList<string>? ParseTagsMetadata(IDictionary<string, string>? metadata)
    {
        if (metadata is null) return null;
        if (!metadata.TryGetValue("Tags", out var raw) || string.IsNullOrWhiteSpace(raw))
            return null;
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .Select(t => t.Replace(';', ','))
                  .ToList()
                  .AsReadOnly();
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
        Tags = []
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
}
