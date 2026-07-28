using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using PoRedoImage.Infrastructure.Services;

namespace PoRedoImage.Tests.Unit.Features;

/// <summary>
/// Unit tests for Imagen3Service — constructor validation, IsConfigured flag, and guard clauses.
/// No actual Gemini API calls are made; cost control: zero tokens consumed.
/// </summary>
public class Imagen3ServiceTests
{
    private readonly Mock<ILogger<GeminiImagen3Service>> _loggerMock = new();
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock = new();

    private IConfiguration BuildConfig(string? apiKey = "test-google-api-key", string? model = null)
    {
        var dict = new Dictionary<string, string?>();
        if (apiKey != null) dict["Google:ApiKey"] = apiKey;
        if (model != null) dict["Google:Imagen3Model"] = model;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private GeminiImagen3Service CreateService(IConfiguration config) =>
        new(config, _httpClientFactoryMock.Object, _loggerMock.Object);

    // ─── IsConfigured flag ──────────────────────────────────────────

    [Fact]
    public void IsConfigured_WhenApiKeySet_ReturnsTrue()
    {
        var svc = CreateService(BuildConfig(apiKey: "some-key"));
        Assert.True(svc.IsConfigured);
    }

    [Fact]
    public void IsConfigured_WhenApiKeyMissing_ReturnsFalse()
    {
        var svc = CreateService(BuildConfig(apiKey: null));
        Assert.False(svc.IsConfigured);
    }

    [Fact]
    public void IsConfigured_WhenApiKeyWhitespace_ReturnsFalse()
    {
        var svc = CreateService(BuildConfig(apiKey: "   "));
        Assert.False(svc.IsConfigured);
    }

    // ─── Mock-mode guard (Item #4) ──────────────────────────────────
    // Mirror of the AzureOpenAiService construction-time guard. Gemini DOES route through
    // HttpClient (covered by MockAiDelegatingHandler), but failing at construction surfaces a
    // regression immediately instead of at the first call.

    [Fact]
    public void Constructor_MockModeEnabled_ThrowsToBlockLiveTokenSpend()
    {
        var dict = new Dictionary<string, string?>
        {
            ["Google:ApiKey"] = "test-google-api-key",
            ["Mocks:UseMockAi"] = "true"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var ex = Assert.Throws<InvalidOperationException>(
            () => CreateService(config));
        Assert.Contains("Mocks:UseMockAi", ex.Message);
    }

    // ─── GenerateAsync guard clauses ───────────────────────────

    [Fact]
    public async Task GenerateAsync_WhenNotConfigured_ThrowsInvalidOperationException()
    {
        var svc = CreateService(BuildConfig(apiKey: null));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.GenerateAsync("a prompt"));
    }

    [Theory]
    [InlineData("   ")] // whitespace-only prompt
    [InlineData("")]    // empty prompt
    public async Task GenerateAsync_BlankPrompt_ThrowsArgumentException(string prompt)
    {
        var svc = CreateService(BuildConfig());
        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentException for whitespace/empty
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.GenerateAsync(prompt));
    }
}
