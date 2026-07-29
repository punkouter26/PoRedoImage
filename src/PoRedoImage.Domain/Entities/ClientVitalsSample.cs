namespace PoRedoImage.Domain.Entities;

/// <summary>
/// A browser-measured page-load sample, attributed to the user and session that produced it.
/// </summary>
/// <remarks>
/// The transport DTO carries no identity — <see cref="UserId"/> and <see cref="SessionId"/> are
/// taken server-side from the authenticated principal and the correlation middleware, never from
/// the request body. A client cannot attribute its samples to another user.
/// </remarks>
public sealed record ClientVitalsSample(
    DateTimeOffset Timestamp,
    string UserId,
    string SessionId,
    string Route,
    double InteractiveMs,
    double LoadMs,
    double DomContentLoadedMs,
    double Cls,
    double? JsHeapMb,
    double? WasmHeapMb);
