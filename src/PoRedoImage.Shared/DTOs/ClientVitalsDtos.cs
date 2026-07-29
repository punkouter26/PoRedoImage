using System.ComponentModel.DataAnnotations;

namespace PoRedoImage.Shared.DTOs;

/// <summary>
/// One browser-measured page-load sample, posted by the WASM client to
/// <c>POST /api/diag/vitals</c> after the app has settled.
/// </summary>
/// <remarks>
/// Every numeric bound is deliberately generous but finite: these values arrive from an
/// untrusted client, and an unbounded double would let a caller poison the dashboard's
/// axis scaling with a single request. <c>ValidationFilter</c> rejects anything outside
/// the ranges below with a 400 before the value reaches storage.
/// </remarks>
public sealed record ClientVitalsSampleRequest
{
    /// <summary>Relative path the sample was taken on, e.g. <c>/image-regeneration</c>.</summary>
    [Required]
    [StringLength(128, MinimumLength = 1)]
    public string Route { get; init; } = string.Empty;

    /// <summary>
    /// Navigation start → the app's first render, in milliseconds — "time to interactive".
    /// </summary>
    /// <remarks>
    /// This, not <see cref="LoadMs"/>, is the meaningful figure for a WebAssembly app.
    /// <c>blazor.web.js</c> fetches and starts the runtime asynchronously, so the document's
    /// load event fires while the app is still booting: on this app <c>loadEventEnd</c> lands
    /// around 30&#160;ms, an order of magnitude below the real cost of becoming usable.
    /// </remarks>
    [Range(0, 600_000)]
    public double InteractiveMs { get; init; }

    /// <summary>Navigation start → the document's <c>load</c> event, in milliseconds.</summary>
    [Range(0, 600_000)]
    public double LoadMs { get; init; }

    /// <summary>Navigation start → <c>DOMContentLoaded</c>, in milliseconds.</summary>
    [Range(0, 600_000)]
    public double DomContentLoadedMs { get; init; }

    /// <summary>Cumulative Layout Shift accumulated over the observation window. Unitless.</summary>
    [Range(0, 100)]
    public double Cls { get; init; }

    /// <summary>
    /// JS heap in megabytes, from the non-standard <c>performance.memory</c>. Chromium only —
    /// null everywhere else, which is why it is nullable rather than defaulted to zero.
    /// </summary>
    [Range(0, 16_384)]
    public double? JsHeapMb { get; init; }

    /// <summary>
    /// WebAssembly linear-memory size in megabytes. Null when the runtime handle is
    /// unavailable. This — not the JS heap — is the ".NET in the browser" cost.
    /// </summary>
    [Range(0, 16_384)]
    public double? WasmHeapMb { get; init; }
}

/// <summary>A single stored sample as returned by the history projection.</summary>
public sealed record ClientVitalsPointDto(
    DateTimeOffset Timestamp,
    string Route,
    double InteractiveMs,
    double LoadMs,
    double DomContentLoadedMs,
    double Cls,
    double? JsHeapMb,
    double? WasmHeapMb);

/// <summary>
/// The history projection served by <c>GET /api/diag/vitals</c> and consumed by the
/// diagnostics dashboard. Samples are newest-first.
/// </summary>
public sealed record ClientVitalsHistoryDto(
    int Days,
    int Count,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ClientVitalsPointDto> Samples);
