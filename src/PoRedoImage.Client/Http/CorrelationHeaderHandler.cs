namespace PoRedoImage.Client.Http;

/// <summary>
/// §6.9: stamps every outbound BFF request with a stable <c>X-Session-ID</c> (per tab) and a fresh
/// <c>X-Correlation-ID</c> (per request). The server's RequestContextMiddleware reads both, echoes
/// them back, and pushes them into the Serilog LogContext so a single browser action is traceable
/// end-to-end. Existing headers are never overwritten (a caller may already correlate a retry).
/// </summary>
public sealed class CorrelationHeaderHandler(ClientRequestContext context) : DelegatingHandler
{
    private const string SessionHeader = "X-Session-ID";
    private const string CorrelationHeader = "X-Correlation-ID";

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!request.Headers.Contains(SessionHeader))
            request.Headers.TryAddWithoutValidation(SessionHeader, context.SessionId);

        if (!request.Headers.Contains(CorrelationHeader))
            request.Headers.TryAddWithoutValidation(CorrelationHeader, Guid.NewGuid().ToString("N"));

        return base.SendAsync(request, cancellationToken);
    }
}
