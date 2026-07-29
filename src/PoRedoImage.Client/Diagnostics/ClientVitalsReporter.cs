using Microsoft.JSInterop;
using PoRedoImage.Shared.DTOs;
using System.Net.Http.Json;

namespace PoRedoImage.Client.Diagnostics;

/// <summary>
/// Collects one browser performance sample per app load and posts it to
/// <c>POST /api/diag/vitals</c>.
/// </summary>
/// <remarks>
/// Fire-and-forget by design. Telemetry must never delay, block or break the experience it is
/// measuring, so every failure path here is swallowed at <c>Debug</c> level — the same posture
/// as <c>AudioFeedbackService</c>. Registered scoped and driven by
/// <c>ClientVitalsProbe</c> from the layout, so it runs once per app load rather than once
/// per navigation: <see cref="PerformanceNavigationTiming"/> describes the document, and a
/// client-side route change does not produce a new one.
/// </remarks>
public sealed class ClientVitalsReporter(
    IJSRuntime js, HttpClient http, ILogger<ClientVitalsReporter> logger)
{
    /// <summary>
    /// How long to let layout settle after <c>load</c> before sampling CLS. Long enough for
    /// late-arriving fonts and images to have shifted the page; short enough that a user who
    /// navigates away immediately still gets counted.
    /// </summary>
    private const int SettleMs = 2500;

    private bool _reported;

    /// <summary>Collects and posts a single sample. Safe to call more than once — later calls no-op.</summary>
    public async Task ReportOnceAsync(CancellationToken ct = default)
    {
        if (_reported) return;
        _reported = true;

        try
        {
            var probe = await js.InvokeAsync<VitalsProbe?>("poRedoImageVitals.collect", ct, SettleMs);
            if (probe is null) return;

            var request = new ClientVitalsSampleRequest
            {
                // Guard the wire contract's own bounds here too. A hostile value cannot originate
                // from our own JS, but a browser quirk producing a negative or absurd timing would
                // otherwise cost a round-trip just to be rejected by the server's validator.
                Route = Truncate(probe.Route, 128),
                InteractiveMs = Clamp(probe.InteractiveMs, 600_000),
                LoadMs = Clamp(probe.LoadMs, 600_000),
                DomContentLoadedMs = Clamp(probe.DomContentLoadedMs, 600_000),
                Cls = probe.ClsSupported ? Clamp(probe.Cls, 100) : 0,
                JsHeapMb = probe.JsHeapMb is { } jsHeap ? Clamp(jsHeap, 16_384) : null,
                WasmHeapMb = probe.WasmHeapMb is { } wasmHeap ? Clamp(wasmHeap, 16_384) : null,
            };

            var response = await http.PostAsJsonAsync("api/diag/vitals", request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug("Client vitals POST returned {Status}.", (int)response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            // Unauthenticated load, offline, rate-limited, JS absent — all uninteresting to the
            // user and all non-fatal. The sample is simply lost.
            logger.LogDebug(ex, "Client vitals sample was not reported.");
        }
    }

    private static double Clamp(double value, double max) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, max) : 0;

    private static string Truncate(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? "/"
        : value.Length <= max ? value
        : value[..max];

    /// <summary>Shape returned by <c>wwwroot/js/vitals.js</c>.</summary>
    private sealed record VitalsProbe(
        string? Route,
        double InteractiveMs,
        double LoadMs,
        double DomContentLoadedMs,
        double Cls,
        bool ClsSupported,
        double? JsHeapMb,
        double? WasmHeapMb);
}
