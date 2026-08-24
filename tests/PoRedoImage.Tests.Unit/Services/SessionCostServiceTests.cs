using Microsoft.Extensions.Logging.Abstractions;
using PoRedoImage.Client.Services;
using PoRedoImage.Shared.DTOs;
using Xunit;

namespace PoRedoImage.Tests.Unit.Services;

public sealed class SessionCostServiceTests
{
    [Fact]
    public void Initial_state_has_zero_costs_and_operations()
    {
        using var http = new HttpClient();
        var sut = new SessionCostService(http, NullLogger<SessionCostService>.Instance);

        Assert.Equal(0, sut.ImageCount);
        Assert.Equal(0, sut.VisionCount);
        Assert.Equal(0, sut.TextReasoningCount);
        Assert.Equal(0, sut.MusicCount);
        Assert.Equal(0, sut.TotalOperations);
        Assert.Equal(0m, sut.EstimatedTotal);
        Assert.Empty(sut.GetBreakdown());
    }

    [Fact]
    public void Recording_different_services_accumulates_operations_and_cost()
    {
        using var http = new HttpClient();
        var sut = new SessionCostService(http, NullLogger<SessionCostService>.Instance);

        sut.RecordImages(2);
        sut.RecordVision(3);
        sut.RecordTextReasoning(4);
        sut.RecordMusic(1);

        Assert.Equal(2, sut.ImageCount);
        Assert.Equal(3, sut.VisionCount);
        Assert.Equal(4, sut.TextReasoningCount);
        Assert.Equal(1, sut.MusicCount);
        Assert.Equal(10, sut.TotalOperations);

        // (2 * 0.039) + (3 * 0.001) + (4 * 0.0015) + (1 * 0.040) = 0.078 + 0.003 + 0.006 + 0.040 = 0.127
        Assert.Equal(0.127m, sut.EstimatedTotal);

        var breakdown = sut.GetBreakdown();
        Assert.Equal(4, breakdown.Count);
        Assert.Contains(breakdown, b => b.Name == "Image Generation" && b.Count == 2);
        Assert.Contains(breakdown, b => b.Name == "Vision Analysis" && b.Count == 3);
        Assert.Contains(breakdown, b => b.Name == "Text & Reasoning" && b.Count == 4);
        Assert.Contains(breakdown, b => b.Name == "Lyria Music" && b.Count == 1);
    }

    [Fact]
    public void Reset_clears_all_service_counts_and_spend()
    {
        using var http = new HttpClient();
        var sut = new SessionCostService(http, NullLogger<SessionCostService>.Instance);

        sut.RecordImages(1);
        sut.RecordVision(1);
        sut.Reset();

        Assert.Equal(0, sut.TotalOperations);
        Assert.Equal(0m, sut.EstimatedTotal);
        Assert.Empty(sut.GetBreakdown());
    }

    [Fact]
    public void OnChange_event_fires_when_any_service_is_recorded()
    {
        using var http = new HttpClient();
        var sut = new SessionCostService(http, NullLogger<SessionCostService>.Instance);
        var fired = 0;
        sut.OnChange += () => fired++;

        sut.RecordVision(1);
        sut.RecordTextReasoning(1);
        sut.RecordMusic(1);
        sut.RecordImages(1);

        Assert.Equal(4, fired);
    }
}

