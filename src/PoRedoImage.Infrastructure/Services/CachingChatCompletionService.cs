using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Interfaces;

namespace PoRedoImage.Infrastructure.Services;

/// <summary>
/// Memoises chat completions that READ AN IMAGE, keyed by prompt + image content hash.
/// </summary>
/// <remarks>
/// Those are the expensive, repeatable calls — the reproduction prompt and the Rap Roast scene read —
/// and a user switching features or re-running on the same photo asked the same question of the same
/// pixels every time. Text-only calls (lyrics) pass straight through: re-rolling bars is supposed to
/// produce new bars. Empty content is the refusal shape and is never cached, so a filtered call
/// retries rather than replaying the refusal for six hours.
/// </remarks>
public sealed class CachingChatCompletionService(
    IChatCompletionService inner, IMemoryCache cache, ILogger<CachingChatCompletionService> logger)
    : IChatCompletionService
{
    public bool IsConfigured => inner.IsConfigured;

    public async Task<ChatCompletionResult> CompleteAsync(
        string systemPrompt, string userPrompt, byte[]? image = null, string? jsonSchema = null, CancellationToken ct = default)
    {
        if (image is null)
            return await inner.CompleteAsync(systemPrompt, userPrompt, image, jsonSchema, ct);

        var key = CacheKeys.ForImage(CacheKeys.ForText("chat", systemPrompt, userPrompt, jsonSchema ?? ""), image);
        if (cache.TryGetValue(key, out ChatCompletionResult? hit) && hit is not null)
        {
            logger.LogInformation("Chat-vision cache hit; skipped an upstream call.");
            return hit with { TokensUsed = 0, ElapsedMs = 0 };
        }

        var result = await inner.CompleteAsync(systemPrompt, userPrompt, image, jsonSchema, ct);
        if (!string.IsNullOrWhiteSpace(result.Content))
            cache.Set(key, result, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = CachingVisionService.Ttl, Size = 1 });
        return result;
    }
}
