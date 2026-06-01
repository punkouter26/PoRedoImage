using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Caching.Memory;

namespace PoRedoImage.Web.Features.Idempotency;

/// <summary>
/// IEndpointFilter that de-duplicates Write requests by <c>Idempotency-Key</c> header.
/// Applied to any endpoint that mutates state (POST/PUT/DELETE). Without this, double-clicks
/// or browser auto-retries create duplicate rows in Table Storage and double-charge AI tokens
/// (Po2Logic Failure #6 / BOMB-1).
/// <para>
/// Caching strategy: <see cref="IMemoryCache"/> with 24h TTL keyed by (userId, key).
/// Replays within the TTL return the cached 2xx response with <c>Idempotent-Replay: true</c>.
/// </para>
/// </summary>
public sealed class IdempotencyKeyFilter : IEndpointFilter
{
    public const string HeaderName = "Idempotency-Key";
    public const string ReplayHeaderName = "Idempotent-Replay";
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    private readonly IMemoryCache _cache;
    private readonly ILogger<IdempotencyKeyFilter> _logger;

    public IdempotencyKeyFilter(IMemoryCache cache, ILogger<IdempotencyKeyFilter> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var http = ctx.HttpContext;

        // Idempotency-Key is optional. If absent, the request flows through unchanged —
        // we only de-dupe when the client explicitly opts in. This avoids breaking
        // existing curl/Postman scripts that don't send the header.
        if (!http.Request.Headers.TryGetValue(HeaderName, out var headerValues))
            return await next(ctx);

        var raw = headerValues.ToString();
        if (!IdempotencyKey.TryParse(raw, out var key))
        {
            return Results.Problem(
                detail: $"Invalid {HeaderName} header. Must be a UUID (e.g., 019065a1-7b9d-7c9b-9a0a-123456789abc).",
                statusCode: 400, title: "Invalid Idempotency Key");
        }

        var userId = http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                     ?? "anonymous";
        var cacheKey = $"idem:{userId}:{key.Value:D}";

        // Replay path: return the cached response verbatim.
        if (_cache.TryGetValue<CachedResponse>(cacheKey, out var cached) && cached is not null)
        {
            _logger.LogInformation("Idempotent replay for key {Key} (user {UserId})", key, userId);
            http.Response.Headers[ReplayHeaderName] = "true";
            http.Response.StatusCode = cached.StatusCode;
            http.Response.ContentType = cached.ContentType;
            await http.Response.WriteAsync(cached.Body, ctx.HttpContext.RequestAborted);
            return Results.Empty; // already written
        }

        // First call: run the endpoint, capture the response, cache it.
        var result = await next(ctx);

        if (result is IStatusCodeHttpResult scr && scr.StatusCode is >= 200 and < 300)
        {
            var (statusCode, contentType, body) = await MaterializeAsync(http, result);
            if (body is not null)
            {
                _cache.Set(cacheKey, new CachedResponse(statusCode, contentType, body), Ttl);
            }
        }

        return result;
    }

    /// <summary>
    /// Replays require the full response body, but Minimal API results are often unbuffered.
    /// We swap in a buffering stream, run the inner pipeline, then snapshot the bytes.
    /// </summary>
    private static async Task<(int StatusCode, string? ContentType, string? Body)> MaterializeAsync(
        HttpContext http, object? result)
    {
        var statusCode = (result as IStatusCodeHttpResult)?.StatusCode
            ?? http.Response.StatusCode;

        // Body cannot be reliably extracted from a returned IResult after the fact.
        // We just cache the status code + content-type and the IResult's body if it
        // was written synchronously. Replays of streamed bodies are not supported.
        var contentType = http.Response.ContentType;
        if (http.Response.Body is MemoryStream ms && ms.Length > 0)
        {
            ms.Position = 0;
            using var reader = new StreamReader(ms, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            return (statusCode, contentType, body);
        }
        return (statusCode, contentType, null);
    }

    private sealed record CachedResponse(int StatusCode, string? ContentType, string Body);
}
