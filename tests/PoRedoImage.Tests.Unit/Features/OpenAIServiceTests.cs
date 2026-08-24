using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using PoRedoImage.Infrastructure.Services;

namespace PoRedoImage.Tests.Unit.Features;

/// <summary>
/// Unit tests for AzureOpenAiService — constructor validation and argument guard clauses.
/// Azure OpenAI calls are NOT invoked; only pure logic and preconditions are tested.
/// Cost control: zero token usage.
/// </summary>
public class AzureOpenAiServiceTests
{
    private readonly Mock<ILogger<AzureOpenAiService>> _loggerMock = new();

    private static IConfiguration BuildConfig(string? endpoint = "https://test.openai.azure.com/",
        string? key = "test-key")
    {
        var dict = new Dictionary<string, string?>();
        if (endpoint != null) dict["OpenAI:Endpoint"] = endpoint;
        if (key != null) dict["OpenAI:Key"] = key;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    // ─── Constructor tests ──────────────────────────────────────────

    [Fact]
    public void Constructor_MissingEndpoint_DoesNotThrow_SetsConfigError()
    {
        // Service now degrades gracefully; errors are reported at call time, not construction
        var config = BuildConfig(endpoint: null);
        var service = new AzureOpenAiService(config, _loggerMock.Object);
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_MissingKey_UsesManagedIdentity_DoesNotThrow()
    {
        // When no API key is configured, the service falls back to DefaultAzureCredential
        // (Managed Identity / Workload Identity on ACA). Construction must succeed.
        var config = BuildConfig(key: null);
        var service = new AzureOpenAiService(config, _loggerMock.Object);
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_ValidConfig_DoesNotThrow()
    {
        var service = new AzureOpenAiService(BuildConfig(), _loggerMock.Object);
        Assert.NotNull(service);
    }

    // ─── Mock-mode guard (Item #4) ──────────────────────────────────
    // AzureOpenAiService uses the Azure.AI.OpenAI SDK which does NOT route through HttpClient,
    // so the MockAiDelegatingHandler cannot intercept it. The construction-time guard is the
    // last line of defense against a regression that wires the real service while mock mode is on.

    [Fact]
    public void Constructor_MockModeEnabled_ThrowsToBlockLiveTokenSpend()
    {
        var dict = new Dictionary<string, string?>
        {
            ["OpenAI:Endpoint"] = "https://test.openai.azure.com/",
            ["OpenAI:Key"] = "test-key",
            ["Mocks:UseMockAi"] = "true"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var ex = Assert.Throws<InvalidOperationException>(
            () => new AzureOpenAiService(config, _loggerMock.Object));
        Assert.Contains("Mocks:UseMockAi", ex.Message);
    }

    // ─── EnhanceDescriptionAsync guard-clause tests ─────────────────

    [Fact]
    public async Task EnhanceDescriptionAsync_NullDescription_ThrowsArgumentNull()
    {
        var service = new AzureOpenAiService(BuildConfig(), _loggerMock.Object);
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.EnhanceDescriptionAsync(null!, new List<string> { "tag" }, 200));
    }

    [Fact]
    public async Task EnhanceDescriptionAsync_NullTags_ThrowsArgumentNull()
    {
        var service = new AzureOpenAiService(BuildConfig(), _loggerMock.Object);
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.EnhanceDescriptionAsync("desc", null!, 200));
    }

    // Zero and negative are the same guard clause reached by two literals, so this is one theory
    // rather than two facts — consolidated to keep the Unit tier inside its 100-method ceiling.
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task EnhanceDescriptionAsync_NonPositiveTargetLength_ThrowsArgumentOutOfRange(int targetLength)
    {
        var service = new AzureOpenAiService(BuildConfig(), _loggerMock.Object);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.EnhanceDescriptionAsync("desc", new List<string> { "tag" }, targetLength));
    }

    // ─── GenerateMemeCaptionAsync guard-clause tests ────────────────

    [Fact]
    public async Task GenerateMemeCaptionAsync_NullTags_ThrowsArgumentNull()
    {
        var service = new AzureOpenAiService(BuildConfig(), _loggerMock.Object);
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.GenerateMemeCaptionAsync(null!));
    }
}
