using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Shared.DTOs;

namespace PoRedoImage.Web.Features.BulkGenerate;

/// <summary>
/// Fan-out image generation for the bulk board: one source image, many prompts.
/// </summary>
/// <remarks>
/// The endpoint owns only framing (results go out as NDJSON); this decides how many calls run at
/// once, what a failed slot yields, and how a re-roll seed is derived.
/// </remarks>
public sealed class BulkGenerationService
{
    /// <summary>
    /// Concurrent calls to the image model per batch. Baseline 4 with adaptive backoff on rate limits.
    /// </summary>
    internal const int BatchConcurrency = 4;

    private readonly IImageGenerationService _generator;
    private readonly ILogger<BulkGenerationService> _logger;

    public BulkGenerationService(IImageGenerationService generator, ILogger<BulkGenerationService> logger)
    {
        _generator = generator;
        _logger = logger;
    }

    public bool IsConfigured => _generator.IsConfigured;

    /// <summary>
    /// Generates one image per prompt, yielding each the moment it lands. Slots complete out of
    /// order; <see cref="BulkBatchItem.Index"/> maps each back to its prompt, and a failed slot
    /// yields an item carrying <see cref="BulkBatchItem.Error"/> instead of aborting its siblings.
    /// </summary>
    public async IAsyncEnumerable<BulkBatchItem> GenerateBatchAsync(
        IReadOnlyList<string> prompts,
        byte[] source,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var generator = _generator;

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
                // Rate limits are retried once, by the GeminiApi resilience handler — this loop used
                // to retry them again on top, for up to nine calls per slot.
                try
                {
                    var (data, contentType, _) = await generator
                        .GenerateImageAsync(prompt, source, ct)
                        .ConfigureAwait(false);

                    await writer
                        .WriteAsync(new BulkBatchItem(index, Convert.ToBase64String(data), contentType, null), ct)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // One slot failing is a normal outcome the board already renders. Reporting it
                    // as an item keeps the other nine running.
                    _logger.LogWarning(ex, "Batch slot {Index} failed", index);
                    await writer
                        .WriteAsync(new BulkBatchItem(index, null, null, "Generation failed for this variation."), ct)
                        .ConfigureAwait(false);
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

    /// <summary>
    /// Generates <paramref name="count"/> variations of one winning prompt, each with a distinct
    /// seed. Failed slots are dropped, so the result may hold fewer than requested.
    /// </summary>
    public async Task<BulkRerollResponse> RerollAsync(
        string seedPrompt,
        byte[] source,
        int count,
        CancellationToken ct = default)
    {
        var generator = _generator;
        var sw = Stopwatch.StartNew();

        using var gate = new SemaphoreSlim(BatchConcurrency, BatchConcurrency);
        var tasks = Enumerable.Range(0, count).Select(async i =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
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
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Re-roll slot {Index} failed", i);
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
