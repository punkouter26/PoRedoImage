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
/// The Minimal API IResult is evaluated lazily — to capture its body for caching we wrap the
/// response stream in a <see cref="TeeStream"/> that copies every write through to the
/// original socket AND into an in-memory buffer. The buffer is then stored in the cache so
/// a replayed request can serve the exact same bytes back without re-running the endpoint.
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

        // First call: install a TeeStream that mirrors every write to both the response
        // socket (so the client sees the response normally) AND an in-memory buffer
        // (so we can cache and replay the bytes later). Execute the IResult ourselves so
        // it flows through the tee, then short-circuit the framework by returning
        // Results.Empty (a no-op IResult that prevents it re-executing and overwriting our
        // response).
        var originalBody = http.Response.Body;
        using var buffer = new MemoryStream();
        http.Response.Body = new TeeStream(originalBody, buffer);

        object? result = null;
        try
        {
            result = await next(ctx);
            if (result is IResult iresult)
            {
                await iresult.ExecuteAsync(http);
            }
        }
        finally
        {
            http.Response.Body = originalBody;
        }

        var statusCode = (result as IStatusCodeHttpResult)?.StatusCode ?? http.Response.StatusCode;
        var bodyText = ReadBuffer(buffer);

        if (statusCode is >= 200 and < 300 && bodyText is not null)
        {
            _cache.Set(cacheKey, new CachedResponse(statusCode, http.Response.ContentType, bodyText), Ttl);
            _logger.LogInformation("Idempotency cache populated for key {Key} (user {UserId}, status={Status}, bytes={Bytes})",
                key, userId, statusCode, bodyText.Length);
        }

        // Tell the framework we already wrote the response so it doesn't try again.
        return Results.Empty;
    }

    private static string? ReadBuffer(MemoryStream buffer)
    {
        if (buffer.Length == 0) return null;
        buffer.Position = 0;
        using var reader = new StreamReader(buffer, leaveOpen: true);
        return reader.ReadToEnd();
    }

    private sealed record CachedResponse(int StatusCode, string? ContentType, string Body);

    /// <summary>
    /// Stream that wraps an underlying write target and a parallel capture target. Every
    /// write goes to both. Reads and Flush are proxied to the underlying stream so the
    /// response stays correctly chunked and the framework's lifecycle (headers, Content-Length,
    /// etc.) flows naturally. Disposal of the underlying stream is left to the host — we
    /// only dispose our capture buffer (via the using-statement on the call site).
    /// </summary>
    private sealed class TeeStream : Stream
    {
        private readonly Stream _sink;
        private readonly Stream _capture;

        public TeeStream(Stream sink, Stream capture)
        {
            _sink = sink;
            _capture = capture;
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _sink.Length;
        public override long Position { get => _sink.Position; set => _sink.Position = value; }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            _sink.Write(buffer, offset, count);
            _capture.Write(buffer, offset, count);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await _sink.WriteAsync(buffer, cancellationToken);
            await _capture.WriteAsync(buffer, cancellationToken);
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await _sink.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
            await _capture.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
        }

        public override void Flush()
        {
            _sink.Flush();
            _capture.Flush();
        }

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            await _sink.FlushAsync(cancellationToken);
            await _capture.FlushAsync(cancellationToken);
        }
    }
}
