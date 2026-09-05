using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Interfaces;

namespace PoRedoImage.Infrastructure.Services;

/// <summary>
/// Decorator that memoises vision results by the SHA-256 of the image bytes.
/// </summary>
/// <remarks>
/// <para>
/// The app's normal flows re-analyse byte-identical images constantly and there was no cache
/// anywhere: "Try Again" re-runs the pipeline on the same photo, the result-chaining row sends one
/// feature's output into the next, Studio's "Any service" fires a second feature at the same active
/// image, and Bulk Generate describes its source once per batch. Every one of those was a fresh
/// metered call for an answer already computed seconds earlier.
/// </para>
/// <para>
/// The key is a content hash, not a file name or a session id, which is what makes it correct: two
/// users uploading the same meme, or one user re-uploading after a page reload, are the same
/// question. Nothing user-specific is stored — the value is a description and a tag list derived
/// solely from the bytes — so there is no cross-tenant leak in sharing the entry.
/// </para>
/// <para>
/// In-process <see cref="IMemoryCache"/> deliberately, not a distributed one. The app runs a single
/// App Service instance, the entries are small, and a cache miss costs exactly what the app cost
/// before this class existed. A Redis dependency to raise a hit rate that is already good on the
/// flows that matter would be a worse trade.
/// </para>
/// </remarks>
/// <param name="scope">
/// Which backend produced the answer. It is part of the key, not decoration: three backends answer
/// the same image differently, and a shared key would let an Ollama caption be served to a caller
/// who explicitly asked for Azure.
/// </param>
public sealed class CachingVisionService(
    IVisionService inner,
    IMemoryCache cache,
    ILogger<CachingVisionService> logger,
    string scope = "vision") : IVisionService
{
    /// <summary>
    /// The service this decorates. Exposed so routing tests can assert WHICH backend an id resolved
    /// to without the wrapper hiding the answer.
    /// </summary>
    internal IVisionService Inner => inner;

    /// <summary>
    /// How long an entry lives. Vision results for fixed bytes never change, so the bound exists to
    /// cap memory, not to protect freshness.
    /// </summary>
    internal static readonly TimeSpan Ttl = TimeSpan.FromHours(6);

    public async Task<(string Description, IReadOnlyList<string> Tags, double ConfidenceScore, long ElapsedMs, string? FallbackReason)>
        AnalyzeAsync(byte[] imageData, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(imageData);
        if (imageData.Length == 0) return await inner.AnalyzeAsync(imageData, ct);

        var key = CacheKeys.ForImage(scope, imageData);

        if (cache.TryGetValue(key, out CachedVision? hit) && hit is not null)
        {
            logger.LogInformation("Vision cache hit for {Scope}; skipped an upstream call.", scope);
            // ElapsedMs is reported as 0, not as the original call's duration: the metrics panel
            // shows what THIS request spent, and claiming 800ms for a dictionary lookup would make
            // the pipeline's own timings a lie.
            return (hit.Description, hit.Tags, hit.Confidence, 0, hit.FallbackReason);
        }

        var (description, tags, confidence, elapsed, fallbackReason) = await inner.AnalyzeAsync(imageData, ct);

        cache.Set(key, new CachedVision(description, tags, confidence, fallbackReason),
            new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = Ttl, Size = 1 });

        return (description, tags, confidence, elapsed, fallbackReason);
    }

    // FallbackReason is cached alongside the result on purpose: a cache hit that served the
    // degraded tag-derived description without its explanation would reintroduce exactly the
    // silent degradation this field exists to prevent.
    private sealed record CachedVision(
        string Description, IReadOnlyList<string> Tags, double Confidence, string? FallbackReason);
}

/// <summary>Content-addressed cache keys shared by the AI decorators.</summary>
internal static class CacheKeys
{
    /// <summary>
    /// <c>{scope}:{sha256}</c>. The scope prefix keeps two different questions about the same bytes
    /// — "what is in this image" and "describe the person in it" — from colliding on one entry.
    /// </summary>
    public static string ForImage(string scope, byte[] image) =>
        $"{scope}:{Convert.ToHexStringLower(SHA256.HashData(image))}";

    /// <summary>Key for a text-in/text-out call, hashed so an arbitrarily long prompt is bounded.</summary>
    public static string ForText(string scope, params string[] parts) =>
        $"{scope}:{Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join('', parts))))}";
}
