using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using PoRedoImage.Client.Services;
using Xunit;

namespace PoRedoImage.Tests.Unit.Services;

public sealed class SessionCostServiceTests
{
    /// <summary>Prices come only from /api/pricing — there is no hardcoded fallback to test against.</summary>
    private const string PricingJson = """
        {"imageLabel":"Gemini","imageGenerationUsd":0.039,"currency":"USD","visionAnalysisUsd":0.001,
         "textReasoningUsd":0.0015,"musicGenerationUsd":0.040,"videoGenerationUsd":0.40}
        """;

    [Fact]
    public async Task Recording_accumulates_operations_and_cost_at_server_prices_and_reset_clears_them()
    {
        using var http = new HttpClient(new StubHandler()) { BaseAddress = new Uri("http://localhost/") };
        var sut = new SessionCostService(http, NullLogger<SessionCostService>.Instance);
        await sut.EnsureLoadedAsync();
        var fired = 0;
        sut.OnChange += () => fired++;

        Assert.Equal(0m, sut.EstimatedTotal);
        Assert.Empty(sut.GetBreakdown());

        sut.RecordImages(2);
        sut.RecordVision(3);
        sut.RecordTextReasoning(4);
        sut.RecordMusic(1);

        Assert.Equal(4, fired);
        Assert.Equal(10, sut.TotalOperations);
        // (2 * 0.039) + (3 * 0.001) + (4 * 0.0015) + (1 * 0.040) = 0.127
        Assert.Equal(0.127m, sut.EstimatedTotal);
        Assert.Equal(
            ["Image Generation", "Vision Analysis", "Text & Reasoning", "Lyria Music"],
            sut.GetBreakdown().Select(b => b.Name));

        sut.Reset();
        Assert.Equal(0, sut.TotalOperations);
        Assert.Equal(0m, sut.EstimatedTotal);
        Assert.Empty(sut.GetBreakdown());
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(PricingJson, System.Text.Encoding.UTF8, "application/json"),
            });
    }
}
