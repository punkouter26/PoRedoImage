using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Entities;
using PoRedoImage.Domain.Interfaces;

namespace PoRedoImage.Infrastructure.Repositories;

/// <summary>
/// Azure Table Storage implementation of IBulkPromptRepository.
/// Repository pattern (GoF): isolates data access from business logic.
/// </summary>
public sealed class AzureTableBulkPromptRepository : IBulkPromptRepository
{
    private const string TableName = "BulkPrompts";

    private readonly TableClient? _tableClient;
    private readonly ILogger<AzureTableBulkPromptRepository> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public AzureTableBulkPromptRepository(IConfiguration configuration, ILogger<AzureTableBulkPromptRepository> logger)
    {
        _logger = logger;
        var connectionString = configuration["Storage:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            _tableClient = new TableServiceClient(connectionString).GetTableClient(TableName);
        }
        else
        {
            _logger.LogWarning("Storage:ConnectionString not configured; bulk prompt persistence is disabled.");
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;
        await _initLock.WaitAsync(ct);
        try
        {
            if (!_initialized && _tableClient is not null)
            {
                await _tableClient.CreateIfNotExistsAsync(ct);
                _initialized = true;
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<BulkPrompt?> GetByRowKeyAsync(string rowKey, CancellationToken ct = default)
    {
        if (_tableClient is null) return null;
        await EnsureInitializedAsync(ct);

        try
        {
            var entity = await _tableClient.GetEntityAsync<BulkPromptTableEntity>("prompts", rowKey, cancellationToken: ct);
            return MapToDomain(entity.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task SaveAsync(BulkPrompt prompt, CancellationToken ct = default)
    {
        if (_tableClient is null) return;
        await EnsureInitializedAsync(ct);

        var entity = new BulkPromptTableEntity
        {
            PartitionKey = prompt.PartitionKey,
            RowKey = prompt.RowKey,
            Name = prompt.Name,
            PromptText = prompt.PromptText,
            CreatedAt = prompt.CreatedAt
        };

        await _tableClient.UpsertEntityAsync(entity, cancellationToken: ct);
        _logger.LogInformation("Saved bulk prompt {RowKey}", prompt.RowKey);
    }

    public async Task DeleteAsync(string rowKey, CancellationToken ct = default)
    {
        if (_tableClient is null) return;
        await EnsureInitializedAsync(ct);
        await _tableClient.DeleteEntityAsync("prompts", rowKey, cancellationToken: ct);
        _logger.LogInformation("Deleted bulk prompt {RowKey}", rowKey);
    }

    private static BulkPrompt MapToDomain(BulkPromptTableEntity entity) =>
        new() { PartitionKey = entity.PartitionKey, RowKey = entity.RowKey, Name = entity.Name, PromptText = entity.PromptText, CreatedAt = entity.CreatedAt };
}

internal sealed class BulkPromptTableEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Name { get; set; } = string.Empty;
    public string PromptText { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
