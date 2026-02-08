using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using PoImageGc.Web.Features.ImageAnalysis;

namespace PoImageGc.Tests.Unit.Features;

/// <summary>
/// Unit tests for ComputerVisionService — constructor validation and argument guard clauses.
/// Verifies that the service rejects invalid configuration and null/empty image data.
/// Azure SDK calls are NOT tested here (they'd require real API keys); only pure logic is tested.
/// </summary>
public class ComputerVisionServiceTests
{
    private readonly Mock<ILogger<ComputerVisionService>> _loggerMock = new();
    private readonly TelemetryClient _telemetryClient;

    public ComputerVisionServiceTests()
    {
        var config = new TelemetryConfiguration { TelemetryChannel = new InMemoryChannel() };
        _telemetryClient = new TelemetryClient(config);
    }

    private static IConfiguration BuildConfig(string? endpoint = "https://test.cognitiveservices.azure.com/",
        string? apiKey = "test-key", string? minTagConfidence = null)
    {
        var dict = new Dictionary<string, string?>();
        if (endpoint != null) dict["ComputerVision:Endpoint"] = endpoint;
        if (apiKey != null) dict["ComputerVision:ApiKey"] = apiKey;
        if (minTagConfidence != null) dict["ComputerVision:MinTagConfidence"] = minTagConfidence;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    // ─── Constructor tests ──────────────────────────────────────────

    [Fact]
    public void Constructor_MissingEndpoint_Throws()
    {
        var config = BuildConfig(endpoint: null);
        Assert.Throws<ArgumentNullException>(() =>
            new ComputerVisionService(config, _loggerMock.Object, _telemetryClient));
    }

    [Fact]
    public void Constructor_MissingApiKey_Throws()
    {
        var config = BuildConfig(apiKey: null);
        Assert.Throws<ArgumentNullException>(() =>
            new ComputerVisionService(config, _loggerMock.Object, _telemetryClient));
    }

    [Fact]
    public void Constructor_ValidConfig_DoesNotThrow()
    {
        var config = BuildConfig();
        var service = new ComputerVisionService(config, _loggerMock.Object, _telemetryClient);
        Assert.NotNull(service);
    }

    // ─── AnalyzeImageAsync guard-clause tests ───────────────────────

    [Fact]
    public async Task AnalyzeImageAsync_NullData_ThrowsArgumentNull()
    {
        var service = new ComputerVisionService(BuildConfig(), _loggerMock.Object, _telemetryClient);
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.AnalyzeImageAsync(null!));
    }

    [Fact]
    public async Task AnalyzeImageAsync_EmptyData_ThrowsArgument()
    {
        var service = new ComputerVisionService(BuildConfig(), _loggerMock.Object, _telemetryClient);
        await Assert.ThrowsAsync<ArgumentException>(() => service.AnalyzeImageAsync([]));
    }
}

/// <summary>
/// Helper in-memory telemetry channel for TelemetryClient construction in tests
/// </summary>
file class InMemoryChannel : ITelemetryChannel
{
    public bool? DeveloperMode { get; set; }
    public string EndpointAddress { get; set; } = "";
    public void Send(ITelemetry item) { }
    public void Flush() { }
    public void Dispose() { }
}
