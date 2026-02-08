using System.Net;
using System.Text.Json;

namespace PoImageGc.Tests.Integration;

/// <summary>
/// Integration tests for the /api/diag diagnostics endpoint
/// </summary>
public class DiagnosticsEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public DiagnosticsEndpointTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task DiagEndpoint_ReturnsOk()
    {
        // Act
        var response = await _client.GetAsync("/api/diag");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DiagEndpoint_ReturnsJsonWithEnvironment()
    {
        // Act
        var response = await _client.GetAsync("/api/diag");
        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);

        // Assert
        Assert.True(doc.RootElement.TryGetProperty("Environment", out var env));
        Assert.Equal("Development", env.GetString());
    }

    [Fact]
    public async Task DiagEndpoint_ContainsConfiguration()
    {
        // Act
        var response = await _client.GetAsync("/api/diag");
        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);

        // Assert
        Assert.True(doc.RootElement.TryGetProperty("Configuration", out var config));
        Assert.True(config.TryGetProperty("ComputerVision:Endpoint", out _));
        Assert.True(config.TryGetProperty("OpenAI:Endpoint", out _));
    }

    [Fact]
    public async Task DiagEndpoint_MasksSecrets()
    {
        // Act
        var response = await _client.GetAsync("/api/diag");
        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);

        // Assert — the test key "test-key" should be masked
        var config = doc.RootElement.GetProperty("Configuration");
        var apiKey = config.GetProperty("ComputerVision:ApiKey").GetString();
        Assert.NotNull(apiKey);
        Assert.Contains("*", apiKey);
    }
}
