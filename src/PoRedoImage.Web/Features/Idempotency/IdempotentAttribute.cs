namespace PoRedoImage.Web.Features.Idempotency;

/// <summary>
/// Marker attribute that triggers the <see cref="IdempotencyKeyFilter"/> for an endpoint.
/// Apply to any Write endpoint (POST/PUT/DELETE) that mutates state. If the client
/// does not provide an <c>Idempotency-Key</c> header the filter is a no-op (backwards
/// compatible); with the header, duplicate requests within 24h are replayed verbatim.
/// <para>
/// Usage: add <c>.AddEndpointFilter&lt;IdempotencyKeyFilter&gt;()</c> to the endpoint
/// route group. The filter reads <c>Idempotency-Key</c> from the request headers and
/// caches the 2xx response for 24h keyed by (userId, key).
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentAttribute : Attribute
{
}
