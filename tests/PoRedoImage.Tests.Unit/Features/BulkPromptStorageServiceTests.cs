using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using PoRedoImage.Web.Features.BulkGenerate;

namespace PoRedoImage.Tests.Unit.Features;

/// <summary>
/// Unit tests for BulkPromptStorageService.
/// Verifies null-safe initialisation when no connection string is configured,
/// and checks that the service returns null (not throws) for load operations.
/// </summary>
public class BulkPromptStorageServiceTests
{
    private readonly Mock<ILogger<BulkPromptStorageService>> _loggerMock = new();

    private static IConfiguration BuildConfig(string? connectionString)
    {
        var dict = new Dictionary<string, string?>();
        if (connectionString != null) dict["Storage:ConnectionString"] = connectionString;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public void Constructor_NullConnectionString_DoesNotThrow()
    {
        // Arrange
        var config = BuildConfig(connectionString: null);

        // Act & Assert — must not throw
        var svc = new BulkPromptStorageService(config, _loggerMock.Object);
        Assert.NotNull(svc);
    }

    [Fact]
    public void Constructor_EmptyConnectionString_DoesNotThrow()
    {
        var config = BuildConfig(connectionString: "");
        var svc = new BulkPromptStorageService(config, _loggerMock.Object);
        Assert.NotNull(svc);
    }

    [Fact]
    public async Task LoadPromptsAsync_NoConnectionString_ReturnsNull()
    {
        // Arrange
        var config = BuildConfig(connectionString: null);
        var svc = new BulkPromptStorageService(config, _loggerMock.Object);

        // Act
        var result = await svc.LoadPromptsAsync("any-session");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task SavePromptsAsync_NoConnectionString_DoesNotThrow()
    {
        // Arrange
        var config = BuildConfig(connectionString: null);
        var svc = new BulkPromptStorageService(config, _loggerMock.Object);

        // Act & Assert — should silently no-op
        await svc.SavePromptsAsync("any-session", ["prompt1", "prompt2"]);
    }
}
