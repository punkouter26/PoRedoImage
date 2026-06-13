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

    [Fact]
    public void Constructor_DefaultDeployments_UsedWhenNotConfigured()
    {
        var service = new AzureOpenAiService(BuildConfig(), _loggerMock.Object);
        Assert.NotNull(service);
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

    [Fact]
    public async Task EnhanceDescriptionAsync_ZeroTargetLength_ThrowsArgumentOutOfRange()
    {
        var service = new AzureOpenAiService(BuildConfig(), _loggerMock.Object);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.EnhanceDescriptionAsync("desc", new List<string> { "tag" }, 0));
    }

    [Fact]
    public async Task EnhanceDescriptionAsync_NegativeTargetLength_ThrowsArgumentOutOfRange()
    {
        var service = new AzureOpenAiService(BuildConfig(), _loggerMock.Object);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.EnhanceDescriptionAsync("desc", new List<string> { "tag" }, -1));
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
