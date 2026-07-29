using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Entities;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Shared.Configuration;

namespace PoRedoImage.Infrastructure.Repositories;

/// <summary>
/// Key derivation for the <c>ClientVitals</c> table. Separated from the repository so the
/// ordering guarantee is unit-testable without a storage account or a container.
/// </summary>
public static class ClientVitalsKeys
{
    /// <summary>
    /// Partition = the sample's UTC day. Every read is a bounded single-day range scan, and
    /// retention is a partition delete rather than a full-table sweep.
    /// </summary>
    public static string PartitionKeyFor(DateTimeOffset timestamp) =>
        timestamp.UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Row key = inverted tick count, then a uniquifier.
    /// </summary>
    /// <remarks>
    /// Table Storage sorts row keys as ordinal strings, ascending, with no way to reverse the
    /// order in a query. Storing <c>MaxValue - Ticks</c> zero-padded to 19 digits makes the
    /// newest row sort <em>first</em> naturally, so "most recent N" is a <c>Take(n)</c> over the
    /// partition instead of a full scan and an in-memory sort. The 8-hex-character suffix keeps
    /// two samples that land on the same tick from colliding.
    /// </remarks>
    public static string RowKeyFor(DateTimeOffset timestamp, string uniquifier) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{DateTimeOffset.MaxValue.Ticks - timestamp.UtcTicks:D19}-{uniquifier}");

    /// <summary>Recovers the timestamp encoded in a row key. Inverse of <see cref="RowKeyFor"/>.</summary>
    public static bool TryParseTimestamp(string rowKey, out DateTimeOffset timestamp)
    {
        timestamp = default;
        var dash = rowKey.IndexOf('-');
        var ticksPart = dash < 0 ? rowKey : rowKey[..dash];

        if (!long.TryParse(ticksPart, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var inverted))
            return false;

        var ticks = DateTimeOffset.MaxValue.Ticks - inverted;
        if (ticks < 0 || ticks > DateTimeOffset.MaxValue.Ticks) return false;

        timestamp = new DateTimeOffset(ticks, TimeSpan.Zero);
        return true;
    }
}

/// <summary>
/// Azure Table Storage implementation of <see cref="IClientVitalsRepository"/>.
/// </summary>
/// <remarks>
/// Mirrors <see cref="AzureTableBulkPromptRepository"/>: a null <see cref="TableClient"/> when no
/// connection string is configured, so an unconfigured deployment degrades to "no telemetry"
/// rather than failing every page load. Singleton — <see cref="TableClient"/> is thread-safe and
/// this avoids a redundant <c>CreateIfNotExists</c> per request.
/// </remarks>
public sealed class AzureTableClientVitalsRepository : IClientVitalsRepository
{
    private const string TableName = "ClientVitals";

    private readonly TableClient? _tableClient;
    private readonly ILogger<AzureTableClientVitalsRepository> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public AzureTableClientVitalsRepository(
        IConfiguration configuration, ILogger<AzureTableClientVitalsRepository> logger)
    {
        _logger = logger;
        var connectionString = configuration[ConfigKeys.StorageConnectionString];
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            _tableClient = new TableServiceClient(connectionString).GetTableClient(TableName);
        }
        else
        {
            _logger.LogWarning(
                "Storage:ConnectionString not configured; client vitals collection is disabled.");
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

    public async Task SaveAsync(ClientVitalsSample sample, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (_tableClient is null) return;
        await EnsureInitializedAsync(ct);

        var entity = new ClientVitalsTableEntity
        {
            PartitionKey = ClientVitalsKeys.PartitionKeyFor(sample.Timestamp),
            RowKey = ClientVitalsKeys.RowKeyFor(sample.Timestamp, Guid.NewGuid().ToString("N")[..8]),
            UserId = sample.UserId,
            SessionId = sample.SessionId,
            Route = sample.Route,
            InteractiveMs = sample.InteractiveMs,
            LoadMs = sample.LoadMs,
            DomContentLoadedMs = sample.DomContentLoadedMs,
            Cls = sample.Cls,
            JsHeapMb = sample.JsHeapMb,
            WasmHeapMb = sample.WasmHeapMb,
        };

        await _tableClient.AddEntityAsync(entity, ct);
        _logger.LogDebug(
            "Stored client vitals for {Route}: load={LoadMs}ms cls={Cls}", sample.Route, sample.LoadMs, sample.Cls);
    }

    public async Task<IReadOnlyList<ClientVitalsSample>> GetRecentAsync(
        int days, int max, CancellationToken ct = default)
    {
        if (_tableClient is null) return [];

        var results = new List<ClientVitalsSample>(capacity: Math.Min(max, 512));
        var today = DateTimeOffset.UtcNow;

        try
        {
            await EnsureInitializedAsync(ct);

            // Walk day partitions newest-first and stop as soon as the cap is met: with the
            // inverted row key each partition already yields newest-first, so no global sort
            // is needed.
            for (var offset = 0; offset < days && results.Count < max; offset++)
            {
                var partition = ClientVitalsKeys.PartitionKeyFor(today.AddDays(-offset));
                var query = _tableClient.QueryAsync<ClientVitalsTableEntity>(
                    e => e.PartitionKey == partition,
                    maxPerPage: Math.Min(max - results.Count, 1000),
                    cancellationToken: ct);

                await foreach (var entity in query.WithCancellation(ct))
                {
                    results.Add(MapToDomain(entity));
                    if (results.Count >= max) break;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A configured-but-unreachable account (Azurite not started, firewall, rotated key)
            // must not 500 the diagnostics dashboard — the page whose whole job is to tell you
            // something is wrong. Degrade to whatever was read, and say why in the log.
            _logger.LogWarning(ex,
                "Client vitals history query failed after {Count} sample(s); returning a partial result.",
                results.Count);
        }

        return results;
    }

    private static ClientVitalsSample MapToDomain(ClientVitalsTableEntity e)
    {
        // Prefer the timestamp encoded in the row key: it is the browser-reported instant,
        // whereas entity.Timestamp is when Table Storage accepted the write.
        var timestamp = ClientVitalsKeys.TryParseTimestamp(e.RowKey, out var parsed)
            ? parsed
            : e.Timestamp ?? DateTimeOffset.UnixEpoch;

        return new ClientVitalsSample(
            timestamp, e.UserId, e.SessionId, e.Route,
            e.InteractiveMs, e.LoadMs, e.DomContentLoadedMs, e.Cls, e.JsHeapMb, e.WasmHeapMb);
    }
}

internal sealed class ClientVitalsTableEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public Azure.ETag ETag { get; set; }

    public string UserId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public double InteractiveMs { get; set; }
    public double LoadMs { get; set; }
    public double DomContentLoadedMs { get; set; }
    public double Cls { get; set; }
    public double? JsHeapMb { get; set; }
    public double? WasmHeapMb { get; set; }
}
