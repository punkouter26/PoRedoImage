using System.Net;
using System.Net.Http.Json;
using PoRedoImage.Shared.DTOs;

namespace PoRedoImage.Tests.Integration;

/// <summary>
/// Contract tests for the client-vitals slice. Two tests, deliberately: the ceiling on this tier
/// is 50 and these cover the two behaviours that would be expensive to get wrong — the write path
/// accepting a valid sample, and the validator rejecting an out-of-range one before it can reach
/// storage and distort the dashboard's axes.
/// </summary>
public class ClientVitalsEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ClientVitalsEndpointTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PostVitals_accepts_a_valid_sample_even_without_storage_configured()
    {
        // The repository is null-tolerant, so an unconfigured test host must still 202 rather than
        // 500 — telemetry is never allowed to fail the page that reports it.
        var sample = new ClientVitalsSampleRequest
        {
            Route = "/image-regeneration",
            LoadMs = 1842,
            DomContentLoadedMs = 690,
            Cls = 0.041,
            JsHeapMb = 34.2,
            WasmHeapMb = 28.7,
        };

        var response = await _client.PostAsJsonWithTokenAsync("/api/diag/vitals", sample);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task PostVitals_rejects_an_out_of_range_sample()
    {
        // A client-supplied CLS of 5000 would flatten every real value on the chart. The
        // ValidationFilter must reject it at the edge, not store it.
        var sample = new ClientVitalsSampleRequest
        {
            Route = "/",
            LoadMs = 1000,
            DomContentLoadedMs = 500,
            Cls = 5000,
        };

        var response = await _client.PostAsJsonWithTokenAsync("/api/diag/vitals", sample);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
