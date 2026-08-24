using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Interfaces;

namespace PoRedoImage.Infrastructure.Services;

/// <summary>
/// Decorator that memoises the task-specific generative calls. Companion to
/// <see cref="CachingVisionService"/>; see that class for why the cache is content-addressed and
/// in-process.
/// </summary>
/// <remarks>
/// <para>
/// <b>Enhancement and person-description are cached; meme captions are not.</b> The first two are
/// deterministic questions about fixed input, and returning the same answer to the same question is
/// what the caller wants — regenerating an image from the same photo at the same detail level should
/// use the same prompt. A meme caption is the opposite: the user pressing "Generate Meme" twice on
/// one photo is explicitly asking for a different joke, and a cache would turn that button into a
/// no-op. Caching is not free when the variation IS the product.
/// </para>
/// <para>
/// Person-description is the highest-value entry of the three: Bulk Generate calls it once per
/// batch, and a user who runs two batches on the same photo (common — change the prompts, run
/// again) previously paid for a second vision call that could only return the same sentence.
/// </para>
/// </remarks>
public sealed class CachingGenerativeAiService(
    IGenerativeAiService inner,
    IMemoryCache cache,
    ILogger<CachingGenerativeAiService> logger) : IGenerativeAiService
{
    public async Task<(string EnhancedDescription, int TokensUsed, long ElapsedMs)> EnhanceDescriptionAsync(
        string description, IReadOnlyList<string> tags, int targetLength, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(tags);

        var key = CacheKeys.ForText("enhance", description, string.Join(',', tags), targetLength.ToString());

        if (cache.TryGetValue(key, out string? hit) && hit is not null)
        {
            logger.LogInformation("Enhancement cache hit; skipped an upstream call.");
            return (hit, 0, 0);
        }

        var (enhanced, tokens, elapsed) = await inner.EnhanceDescriptionAsync(description, tags, targetLength, ct);

        cache.Set(key, enhanced,
            new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = CachingVisionService.Ttl, Size = 1 });

        return (enhanced, tokens, elapsed);
    }

    /// <summary>Deliberately uncached — see the class remarks.</summary>
    public Task<(string TopText, string BottomText, int TokensUsed, long ElapsedMs)> GenerateMemeCaptionAsync(
        IReadOnlyList<string> tags, CancellationToken ct = default)
        => inner.GenerateMemeCaptionAsync(tags, ct);

    public async Task<string> DescribePersonAsync(byte[] imageData, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(imageData);
        if (imageData.Length == 0) return await inner.DescribePersonAsync(imageData, ct);

        var key = CacheKeys.ForImage("describe-person", imageData);

        if (cache.TryGetValue(key, out string? hit) && hit is not null)
        {
            logger.LogInformation("Person-description cache hit; skipped an upstream call.");
            return hit;
        }

        var described = await inner.DescribePersonAsync(imageData, ct);

        // An empty description is the documented failure signal on this path (the endpoint swallows
        // errors and returns ""), so it must not be cached — that would pin a transient outage in
        // place for six hours.
        if (!string.IsNullOrWhiteSpace(described))
        {
            cache.Set(key, described,
                new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = CachingVisionService.Ttl, Size = 1 });
        }

        return described;
    }
}
