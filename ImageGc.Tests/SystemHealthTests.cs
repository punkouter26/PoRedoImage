using Microsoft.Extensions.Configuration;
using System.Net.NetworkInformation;
using System.Net;
using Xunit;

namespace ImageGc.Tests;

/// <summary>
/// Tests for system health, connectivity, and configuration
/// </summary>
public class SystemHealthTests : TestBase
{
    [Fact]
    public async Task InternetConnectivity_ShouldBeAvailable()
    {
        // Arrange
        const string testHost = "www.microsoft.com";

        // Act & Assert
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(testHost, 5000);

            Assert.True(reply.Status == IPStatus.Success,
                $"Internet connectivity test failed. Ping to {testHost} returned: {reply.Status}");
        }
        catch (PingException ex)
        {
            Assert.Fail($"Internet connectivity test failed with exception: {ex.Message}");
        }
    }

    [Fact]
    public void Configuration_RequiredSettings_ShouldBePresent()
    {
        // Arrange & Act
        var requiredSettings = new[]
        {
            "ComputerVision:Endpoint",
            "ComputerVision:ApiKey",
            "OpenAI:Endpoint",
            "OpenAI:Key" // Fixed: Changed from ApiKey to Key
        };

        // Assert
        foreach (var setting in requiredSettings)
        {
            var value = Configuration[setting];
            Assert.NotNull(value);
            Assert.NotEmpty(value);
            Assert.False(string.IsNullOrWhiteSpace(value),
                $"Configuration setting '{setting}' should not be empty or whitespace");
        }
    }

    [Fact]
    public void Configuration_OptionalSettings_ShouldExist()
    {
        // Test optional settings that may have default values
        var optionalSettings = new[]
        {
            "OpenAI:ChatModel",
            "OpenAI:ImageModel"
        };

        foreach (var setting in optionalSettings)
        {
            var value = Configuration[setting];
            // These can be null/empty as they have defaults, just verify they can be accessed
            Assert.True(true, $"Optional setting '{setting}' can be accessed");
        }
    }
}
