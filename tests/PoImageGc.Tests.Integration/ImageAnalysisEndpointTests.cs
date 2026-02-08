using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using PoImageGc.Web.Features.ImageAnalysis;
using PoImageGc.Web.Models;

namespace PoImageGc.Tests.Integration;

/// <summary>
/// Integration tests for the /api/images/analyze endpoint.
/// All external API services (Computer Vision, OpenAI, Meme Generator) are mocked
/// to avoid token leakage and real API costs.
/// </summary>
public class ImageAnalysisEndpointTests : IClassFixture<MockedServicesWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ImageAnalysisEndpointTests(MockedServicesWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    // ─── Validation tests ───────────────────────────────────────────

    [Fact]
    public async Task AnalyzeImage_EmptyImageData_Returns400()
    {
        var request = new ImageAnalysisRequest { ImageData = "", ContentType = "image/png" };
        var response = await _client.PostAsJsonAsync("/api/images/analyze", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AnalyzeImage_InvalidBase64_Returns400()
    {
        var request = new ImageAnalysisRequest { ImageData = "not-valid-base64!!!", ContentType = "image/png" };
        var response = await _client.PostAsJsonAsync("/api/images/analyze", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ─── ImageRegeneration mode tests ───────────────────────────────

    [Fact]
    public async Task AnalyzeImage_ImageRegenerationMode_Returns200WithResult()
    {
        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var request = new ImageAnalysisRequest
        {
            ImageData = Convert.ToBase64String(imageBytes),
            ContentType = "image/png",
            Mode = ProcessingMode.ImageRegeneration,
            DescriptionLength = 200
        };

        var response = await _client.PostAsJsonAsync("/api/images/analyze", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        // In ImageRegeneration mode, the description is replaced by the enhanced version from OpenAI
        Assert.Equal("An enhanced detailed description of the image", root.GetProperty("description").GetString());
        Assert.True(root.GetProperty("tags").GetArrayLength() > 0);

        // Verify enhanced description from OpenAI flows through
        Assert.NotNull(root.GetProperty("description").GetString());

        // Verify regenerated image is present
        Assert.NotNull(root.GetProperty("regeneratedImageData").GetString());
        Assert.Equal("image/png", root.GetProperty("regeneratedImageContentType").GetString());
    }

    // ─── MemeGeneration mode tests ──────────────────────────────────

    [Fact]
    public async Task AnalyzeImage_MemeGenerationMode_Returns200WithMeme()
    {
        var imageBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        var request = new ImageAnalysisRequest
        {
            ImageData = Convert.ToBase64String(imageBytes),
            ContentType = "image/jpeg",
            Mode = ProcessingMode.MemeGeneration
        };

        var response = await _client.PostAsJsonAsync("/api/images/analyze", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        // Verify meme caption is present
        Assert.NotNull(root.GetProperty("memeCaption").GetString());
        Assert.Contains("FUNNY TOP", root.GetProperty("memeCaption").GetString());

        // Verify meme image data is present
        Assert.NotNull(root.GetProperty("memeImageData").GetString());
    }

    // ─── Health sub-endpoint ────────────────────────────────────────

    [Fact]
    public async Task ImageAnalysisHealth_Returns200()
    {
        var response = await _client.GetAsync("/api/images/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Healthy", content);
    }

    // ─── Metrics verification ───────────────────────────────────────

    [Fact]
    public async Task AnalyzeImage_ReturnsMetrics()
    {
        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var request = new ImageAnalysisRequest
        {
            ImageData = Convert.ToBase64String(imageBytes),
            ContentType = "image/png",
            Mode = ProcessingMode.ImageRegeneration,
            DescriptionLength = 200
        };

        var response = await _client.PostAsJsonAsync("/api/images/analyze", request);
        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);

        var metrics = doc.RootElement.GetProperty("metrics");
        Assert.True(metrics.GetProperty("imageAnalysisTimeMs").GetInt64() >= 0);
        Assert.True(metrics.GetProperty("descriptionGenerationTimeMs").GetInt64() >= 0);
    }
}

/// <summary>
/// WebApplicationFactory that registers mocked services for all external APIs.
/// Cost control: zero real API calls, zero tokens consumed.
/// </summary>
public class MockedServicesWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureHostConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AZURE_KEY_VAULT_ENDPOINT"] = "",
                ["ComputerVision:Endpoint"] = "https://test.cognitiveservices.azure.com/",
                ["ComputerVision:ApiKey"] = "test-key",
                ["OpenAI:Endpoint"] = "https://test.openai.azure.com/",
                ["OpenAI:Key"] = "test-key",
                ["ApplicationInsights:ConnectionString"] = ""
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove real service registrations and replace with mocks
            ReplaceService<IComputerVisionService>(services, CreateMockComputerVision());
            ReplaceService<IOpenAIService>(services, CreateMockOpenAI());
            ReplaceService<IMemeGeneratorService>(services, CreateMockMemeGenerator());
        });

        return base.CreateHost(builder);
    }

    private static void ReplaceService<T>(IServiceCollection services, T mockInstance) where T : class
    {
        // Remove all existing registrations for the interface
        var descriptors = services.Where(d => d.ServiceType == typeof(T)).ToList();
        foreach (var d in descriptors)
            services.Remove(d);

        services.AddScoped(_ => mockInstance);
    }

    private static IComputerVisionService CreateMockComputerVision()
    {
        var mock = new Mock<IComputerVisionService>();
        mock.Setup(s => s.AnalyzeImageAsync(It.IsAny<byte[]>()))
            .ReturnsAsync(("A test description", new List<string> { "cat", "animal", "pet" }, 0.92, 150L));
        return mock.Object;
    }

    private static IOpenAIService CreateMockOpenAI()
    {
        var mock = new Mock<IOpenAIService>();

        mock.Setup(s => s.EnhanceDescriptionAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<int>()))
            .ReturnsAsync(("An enhanced detailed description of the image", 120, 250L));

        mock.Setup(s => s.GenerateDetailedDescriptionAsync(It.IsAny<List<string>>(), It.IsAny<int>(), It.IsAny<double>()))
            .ReturnsAsync(("A detailed description from tags", 100, 200L));

        mock.Setup(s => s.GenerateImageAsync(It.IsAny<string>()))
            .ReturnsAsync((new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x00 }, "image/png", 0, 500L));

        mock.Setup(s => s.GenerateMemeCaptionAsync(It.IsAny<List<string>>(), It.IsAny<double>()))
            .ReturnsAsync(("FUNNY TOP", "FUNNY BOTTOM", 50, 180L));

        return mock.Object;
    }

    private static IMemeGeneratorService CreateMockMemeGenerator()
    {
        var mock = new Mock<IMemeGeneratorService>();
        mock.Setup(s => s.AddCaptionToImage(It.IsAny<byte[]>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns((byte[] img, string? _, string? _) => img); // Return original image
        return mock.Object;
    }
}
