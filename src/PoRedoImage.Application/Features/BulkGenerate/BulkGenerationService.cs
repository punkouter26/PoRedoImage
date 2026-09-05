using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Shared.DTOs;

namespace PoRedoImage.Application.Features.BulkGenerate;

/// <inheritdoc />
public sealed class BulkGenerationService : IBulkGenerationService
{
    /// <summary>
    /// Concurrent calls to the image model per batch. Baseline 4 with adaptive backoff on rate limits.
    /// </summary>
    internal const int BatchConcurrency = 4;

    private readonly IImageGenerationRouter _router;
    private readonly ILogger<BulkGenerationService> _logger;

    public BulkGenerationService(IImageGenerationRouter router, ILogger<BulkGenerationService> logger)
    {
        _router = router;
        _logger = logger;
    }

    public bool IsConfigured(string? imageGenModelId) => _router.Resolve(imageGenModelId).IsConfigured;

    public async IAsyncEnumerable<BulkBatchItem> GenerateBatchAsync(
        IReadOnlyList<string> prompts,
        byte[] source,
        string? imageGenModelId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var generator = _router.Resolve(imageGenModelId);

        // Unbounded because the consumer is a network write that is always slower than generation;
        // a bounded channel would just stall a finished slot behind the socket.
        var channel = Channel.CreateUnbounded<BulkBatchItem>();
        var sw = Stopwatch.StartNew();
        var succeeded = 0;

        var producer = FanOutAsync(generator, prompts, source, channel.Writer, ct);

        await foreach (var item in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            if (item.ImageData is not null)
            {
                succeeded++;
            }

            yield return item;
        }

        // Surfaces a producer-side fault the channel could not carry (cancellation included).
        await producer.ConfigureAwait(false);

        sw.Stop();
        _logger.LogInformation(
            "Batch complete. Requested={Requested}, Succeeded={Succeeded}, Elapsed={Elapsed}ms",
            prompts.Count, succeeded, sw.ElapsedMilliseconds);
    }

    private static bool IsRateLimitException(Exception ex)
    {
        var msg = ex.Message;
        return msg.Contains("429") ||
               msg.Contains("RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("TooManyRequests", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("quota", StringComparison.OrdinalIgnoreCase);
    }

    private async Task FanOutAsync(
        IImageGenerationService generator,
        IReadOnlyList<string> prompts,
        byte[] source,
        ChannelWriter<BulkBatchItem> writer,
        CancellationToken ct)
    {
        using var gate = new SemaphoreSlim(BatchConcurrency, BatchConcurrency);
        try
        {
            var tasks = prompts.Select(async (prompt, index) =>
            {
                await gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    const int maxRetries = 2;
                    var delay = TimeSpan.FromMilliseconds(500);

                    for (var attempt = 0; attempt <= maxRetries; attempt++)
                    {
                        try
                        {
                            var (data, contentType, _) = await generator
                                .GenerateImageAsync(prompt, source, ct)
                                .ConfigureAwait(false);

                            await writer
                                .WriteAsync(new BulkBatchItem(index, Convert.ToBase64String(data), contentType, null), ct)
                                .ConfigureAwait(false);
                            return;
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex) when (attempt < maxRetries && IsRateLimitException(ex))
                        {
                            _logger.LogWarning(ex, "Batch slot {Index} rate limited (attempt {Attempt}); backing off {DelayMs}ms", index, attempt + 1, delay.TotalMilliseconds);
                            await Task.Delay(delay, ct).ConfigureAwait(false);
                            delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 2);
                        }
                        catch (Exception ex)
                        {
                            // One slot failing is a normal outcome the board already renders. Reporting it
                            // as an item keeps the other nine running.
                            _logger.LogWarning(ex, "Batch slot {Index} failed", index);
                            await writer
                                .WriteAsync(new BulkBatchItem(index, null, null, "Generation failed for this variation."), ct)
                                .ConfigureAwait(false);
                            return;
                        }
                    }
                }
                finally
                {
                    gate.Release();
                }
            }).ToArray();

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        finally
        {
            // Always close the reader's loop, including on cancellation — otherwise the consumer
            // waits forever on a channel nobody will write to again.
            writer.TryComplete();
        }
    }

    public async Task<BulkRerollResponse> RerollAsync(
        string seedPrompt,
        byte[] source,
        int count,
        string? imageGenModelId,
        CancellationToken ct = default)
    {
        var generator = _router.Resolve(imageGenModelId);
        var sw = Stopwatch.StartNew();

        using var gate = new SemaphoreSlim(BatchConcurrency, BatchConcurrency);
        var tasks = Enumerable.Range(0, count).Select(async i =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                const int maxRetries = 2;
                var delay = TimeSpan.FromMilliseconds(500);

                for (var attempt = 0; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        // Seed = tick count mixed with the slot index — unique within the batch and
                        // reproducible if the user retries within the same tick.
                        var seed = (int)((Environment.TickCount ^ (i * 2654435761)) & 0x7FFFFFFF);
                        var (data, contentType, _) = await generator
                            .GenerateImageAsync(seedPrompt, source, seed, ct)
                            .ConfigureAwait(false);

                        return new BulkRerollVariation(i, Convert.ToBase64String(data), contentType);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex) when (attempt < maxRetries && IsRateLimitException(ex))
                    {
                        _logger.LogWarning(ex, "Re-roll slot {Index} rate limited (attempt {Attempt}); backing off {DelayMs}ms", i, attempt + 1, delay.TotalMilliseconds);
                        await Task.Delay(delay, ct).ConfigureAwait(false);
                        delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 2);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Re-roll slot {Index} failed", i);
                        return null;
                    }
                }

                return null;
            }
            finally
            {
                gate.Release();
            }
        }).ToArray();

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        var variations = results.Where(r => r is not null).Select(r => r!).ToList();

        sw.Stop();
        _logger.LogInformation(
            "Re-roll batch complete. Requested={Requested}, Succeeded={Succeeded}, Elapsed={Elapsed}ms",
            count, variations.Count, sw.ElapsedMilliseconds);

        return new BulkRerollResponse(variations, count, variations.Count, sw.ElapsedMilliseconds);
    }
}
