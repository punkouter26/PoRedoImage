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
/// Caching strategy: <see cref="IMemoryCache"/> with 24h TTL keyed by (userId, key). Replays
/// within the TTL return the cached 2xx response with <c>Idempotent-Replay: true</c>.
/// </para>
/// <para>
/// The Minimal API Result is evaluated lazily — to capture its body for caching we have to
/// swap <c>http.Response.Body</c> for an in-memory buffer BEFORE the inner pipeline runs,
/// then on success copy the buffer to the original stream and stash the bytes for replays.
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

        // Replay path: return the cached response verbatim and short-circuit the endpoint.
        if (_cache.TryGetValue<CachedResponse>(cacheKey, out var cached) && cached is not null)
        {
            _logger.LogInformation("Idempotent replay for key {Key} (user {UserId})", key, userId);
            http.Response.StatusCode = cached.StatusCode;
            http.Response.Headers[ReplayHeaderName] = "true";
            if (!string.IsNullOrEmpty(cached.ContentType))
                http.Response.ContentType = cached.ContentType;
            await http.Response.WriteAsync(cached.Body, ctx.HttpContext.RequestAborted);
            return Results.Empty; // already written
        }

        // First call: redirect the response body into a buffering stream so the Minimal API
        // IResult has somewhere to dump its lazy JSON, then snapshot it for the cache and
        // copy the same bytes to the real socket.
        var originalBody = http.Response.Body;
        await using var buffer = new MemoryStream();
        http.Response.Body = buffer;

        object? result = null;
        try
        {
            result = await next(ctx);

            // Minimal API IResults are LAZY — `await next()` only hands back the IResult, it
            // doesn't run it. We must materialise it ourselves to capture the body for
            // idempotent replay. Branch on the type so unit/integration tests still pass
            // when they inject stubs.
            if (result is IResult iresult)
            {
                await iresult.ExecuteAsync(http);
            }
            else
            {
                // Non-IResult return value: leave for the framework to handle.
                // Restore the original body so it actually reaches the socket.
                http.Response.Body = originalBody;
                return result;
            }
        }
        finally
        {
            // Restore the original stream irrespective of success/failure so subsequent
            // middleware (request logging, etc.) sees a sane response.
            http.Response.Body = originalBody;
        }

        var statusCode = (result as IStatusCodeHttpResult)?.StatusCode ?? http.Response.StatusCode;
        var bodyText = ReadBuffer(buffer);

        // Copy what the endpoint wrote into the buffer out to the real response. If we
        // don't, the client sees an empty body even on success.
        if (buffer.Length > 0)
        {
            buffer.Position = 0;
            await buffer.CopyToAsync(originalBody, http.RequestAborted);
        }

        if (statusCode is >= 200 and < 300 && bodyText is not null)
        {
            _cache.Set(cacheKey, new CachedResponse(statusCode, http.Response.ContentType, bodyText), Ttl);
            _logger.LogInformation("Idempotency cache populated for key {Key} (user {UserId}, status={Status}, bytes={Bytes})",
                key, userId, statusCode, bodyText.Length);
        }

        return result;
    }

    private static string? ReadBuffer(MemoryStream buffer)
    {
        if (buffer.Length == 0) return null;
        buffer.Position = 0;
        using var reader = new StreamReader(buffer, leaveOpen: true);
        return reader.ReadToEnd();
    }

    private sealed record CachedResponse(int StatusCode, string? ContentType, string Body);
}
