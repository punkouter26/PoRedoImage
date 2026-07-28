namespace PoRedoImage.Web.Features.Diagnostics;

/// <summary>
/// Completes the correlation chain required by §3 ("pass X-Session-ID and X-Correlation-ID through
/// all HTTP calls"). <see cref="RequestContextMiddleware"/> covers the browser → BFF leg; this handler
/// covers BFF → downstream (Gemini, HuggingFace, Ollama), so a single correlation id spans the whole
/// request path instead of stopping at the server boundary.
/// </summary>
/// <remarks>
/// Values are read from <see cref="HttpContext.Items"/> rather than the request headers so the
/// generated fallbacks (a fresh GUID, or <c>TraceIdentifier</c> for non-WASM callers) propagate too.
/// Outside a request — background work, startup health checks — there is no ambient context and the
/// handler is a no-op rather than inventing an id that correlates to nothing.
/// </remarks>
public sealed class OutboundCorrelationHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var items = accessor.HttpContext?.Items;
        if (items is not null)
        {
            Stamp(request, RequestContextMiddleware.CorrelationHeader, items);
            Stamp(request, RequestContextMiddleware.SessionHeader, items);
        }

        return base.SendAsync(request, cancellationToken);
    }

    // TryAddWithoutValidation, not Add: a caller that already set the header wins, and a duplicate
    // Add would throw rather than overwrite.
    private static void Stamp(HttpRequestMessage request, string header, IDictionary<object, object?> items)
    {
        if (items.TryGetValue(header, out var value) && value is string id && !string.IsNullOrEmpty(id))
        {
            request.Headers.TryAddWithoutValidation(header, id);
        }
    }
}
