using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using PoRedoImage.Web.Features.BulkGenerate;

namespace PoRedoImage.Tests.Unit.Features;

/// <summary>
/// Unit tests for Imagen3Service — constructor validation, IsConfigured flag, and guard clauses.
/// No actual Gemini API calls are made; cost control: zero tokens consumed.
/// </summary>
public class Imagen3ServiceTests
{
    private readonly Mock<ILogger<Imagen3Service>> _loggerMock = new();
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock = new();

    private IConfiguration BuildConfig(string? apiKey = "test-google-api-key", string? model = null)
    {
        var dict = new Dictionary<string, string?>();
        if (apiKey != null) dict["Google:ApiKey"] = apiKey;
        if (model != null) dict["Google:Imagen3Model"] = model;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private Imagen3Service CreateService(IConfiguration config) =>
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

    // ─── Constructor — does not throw regardless of config ──────────

    [Fact]
    public void Constructor_WithValidConfig_DoesNotThrow()
    {
        var svc = CreateService(BuildConfig());
        Assert.NotNull(svc);
    }

    [Fact]
    public void Constructor_WithNoApiKey_DoesNotThrow()
    {
        var svc = CreateService(BuildConfig(apiKey: null));
        Assert.NotNull(svc);
    }

    [Fact]
    public void Constructor_CustomModel_DoesNotThrow()
    {
        var svc = CreateService(BuildConfig(model: "imagen-3.0-generate-002"));
        Assert.NotNull(svc);
        Assert.True(svc.IsConfigured);
    }

    // ─── GenerateImageAsync guard clauses ───────────────────────────

    [Fact]
    public async Task GenerateImageAsync_WhenNotConfigured_ThrowsInvalidOperationException()
    {
        var svc = CreateService(BuildConfig(apiKey: null));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.GenerateImageAsync("a prompt"));
    }

    [Fact]
    public async Task GenerateImageAsync_WhitespacePrompt_ThrowsArgumentException()
    {
        var svc = CreateService(BuildConfig());
        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentException for whitespace
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.GenerateImageAsync("   "));
    }

    [Fact]
    public async Task GenerateImageAsync_EmptyPrompt_ThrowsArgumentException()
    {
        var svc = CreateService(BuildConfig());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.GenerateImageAsync(""));
    }
}
