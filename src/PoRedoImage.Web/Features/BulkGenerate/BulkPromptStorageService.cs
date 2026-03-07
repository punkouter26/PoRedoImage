using Azure;
using Azure.Data.Tables;
using System.Text.Json;

namespace PoRedoImage.Web.Features.BulkGenerate;

public class BulkPromptStorageService : IBulkPromptStorageService
{
    private const string TableName = "BulkPrompts";
    private const string PartitionKey = "prompts";

    private readonly TableClient? _tableClient;
    private readonly ILogger<BulkPromptStorageService> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public BulkPromptStorageService(IConfiguration configuration, ILogger<BulkPromptStorageService> logger)
    {
        _logger = logger;
        var connectionString = configuration["Storage:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            var serviceClient = new TableServiceClient(connectionString);
            _tableClient = serviceClient.GetTableClient(TableName);
        }
        else
        {
            _logger.LogInformation("Storage:ConnectionString not configured; Bulk Generate prompt persistence is disabled.");
        }
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized) return;
        await _initLock.WaitAsync();
        try
        {
            if (!_initialized && _tableClient is not null)
            {
                await _tableClient.CreateIfNotExistsAsync();
                _initialized = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to ensure Table Storage table exists; prompts will not persist.");
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<string[]?> LoadPromptsAsync(string userId)
    {
        if (_tableClient is null) return null;
        await EnsureInitializedAsync();

        try
        {
            var entity = await _tableClient.GetEntityAsync<BulkPromptEntity>(PartitionKey, userId);
            if (entity?.Value?.PromptsJson is not null)
                return JsonSerializer.Deserialize<string[]>(entity.Value.PromptsJson);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // No prompts saved yet for this user — not an error
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading prompts for user {UserId}", userId);
        }

        return null;
    }

    public async Task SavePromptsAsync(string userId, string[] prompts)
    {
        if (_tableClient is null) return;
        await EnsureInitializedAsync();

        try
        {
            var entity = new BulkPromptEntity
            {
                PartitionKey = PartitionKey,
                RowKey = userId,
                PromptsJson = JsonSerializer.Serialize(prompts),
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await _tableClient.UpsertEntityAsync(entity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving prompts for user {UserId}", userId);
        }
    }
}

internal class BulkPromptEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string? PromptsJson { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
