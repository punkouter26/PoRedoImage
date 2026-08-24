using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using PoRedoImage.Infrastructure.Services;

namespace PoRedoImage.Tests.Unit.Features;

/// <summary>
/// Unit tests for AzureVisionService — constructor validation and argument guard clauses.
/// Verifies that the service rejects invalid configuration and null/empty image data.
/// Azure SDK calls are NOT tested here (they'd require real API keys); only pure logic is tested.
/// </summary>
public class AzureVisionServiceTests
{
    private readonly Mock<ILogger<AzureVisionService>> _loggerMock = new();

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

    // One behaviour — construction never throws, whatever is missing — reached by three
    // configurations. Was three facts differing only in which key was omitted; consolidated to keep
    // the Unit tier inside its 100-method ceiling.
    [Theory]
    [InlineData(false, true)]   // no endpoint
    [InlineData(true, false)]   // no api key
    [InlineData(true, true)]    // fully configured
    public void Constructor_DegradesGracefully_RatherThanThrowing(bool hasEndpoint, bool hasApiKey)
    {
        // Errors are reported at call time, not construction, so a half-configured environment
        // still starts and tells the user which secret is missing when they try to use it.
        var config = BuildConfig(
            endpoint: hasEndpoint ? "https://test.cognitiveservices.azure.com/" : null,
            apiKey: hasApiKey ? "test-key" : null);

        Assert.NotNull(new AzureVisionService(config, _loggerMock.Object));
    }

    // ─── AnalyzeImageAsync guard-clause tests ───────────────────────

    [Fact]
    public async Task AnalyzeAsync_NullData_ThrowsArgumentNull()
    {
        var service = new AzureVisionService(BuildConfig(), _loggerMock.Object);
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.AnalyzeAsync(null!));
    }

    [Fact]
    public async Task AnalyzeAsync_EmptyData_ThrowsArgument()
    {
        var service = new AzureVisionService(BuildConfig(), _loggerMock.Object);
        await Assert.ThrowsAsync<ArgumentException>(() => service.AnalyzeAsync([]));
    }
}
