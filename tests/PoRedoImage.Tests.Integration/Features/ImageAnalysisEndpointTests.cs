using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Moq;
using PoRedoImage.Web.Configuration;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Infrastructure.Services;
using PoRedoImage.Shared.Configuration;
using PoRedoImage.Shared.DTOs;

namespace PoRedoImage.Tests.Integration;

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

    [Theory]
    [InlineData("")]                      // empty image data
    [InlineData("not-valid-base64!!!")]   // invalid base64
    public async Task AnalyzeImage_BadImageData_Returns400(string imageData)
    {
        var request = new ImageAnalysisRequest { ImageData = imageData, ContentType = "image/png" };
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
        builder.UseEnvironment(PoEnvironments.Test);

        // ConfigureAppConfiguration runs AFTER appsettings.{Environment}.json, so these in-memory
        // values win on conflict. Critically, Storage:ConnectionString="" stops startup from trying
        // to reach a (non-running) Azurite, and KeyVault:Uri="" skips the Key Vault provider — both
        // are required for the host to build at all. (ConfigureHostConfiguration runs FIRST and would
        // be overwritten by appsettings, which is why the host previously failed to build.)
        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["KeyVault:Uri"] = "",
                ["AZURE_KEY_VAULT_ENDPOINT"] = "",
                ["ComputerVision:Endpoint"] = "https://test.cognitiveservices.azure.com/",
                ["ComputerVision:ApiKey"] = "test-key",
                ["OpenAI:Endpoint"] = "https://test.openai.azure.com/",
                ["OpenAI:Key"] = "test-key",
                ["ApplicationInsights:ConnectionString"] = "",
                ["Storage:ConnectionString"] = "",
                ["Google:ApiKey"] = "test-key",
                // Force the zero-network mock AI services at the builder phase (Program.cs reads this
                // flag before our ConfigureServices runs). We then override the three AI mocks below
                // with test-specific behaviours; this keeps the real Azure clients from ever registering.
                ["Mocks:UseMockAi"] = "true"
            });
        });

        builder.ConfigureServices(services =>
        {
            // The /analyze endpoint requires authorization — authenticate every request as the test user.
            AddTestAuth(services);

            // Remove real service registrations and replace with mocks (zero network, zero tokens).
            var mockVision = CreateMockComputerVision();
            ReplaceService<IVisionService>(services, mockVision);

            // The orchestrator resolves vision through IVisionServiceRouter, never IVisionService
            // directly. Mocks:UseMockAi is applied too late to affect builder-phase registration, so
            // the real VisionServiceRouter survives and Resolve() hands back the live
            // AzureVisionService — which attempted a real call, got 401, and surfaced as 503.
            ReplaceService<IVisionServiceRouter>(services, new SingleVisionServiceRouter(mockVision));
            ReplaceService<IGenerativeAiService>(services, CreateMockOpenAI());
            ReplaceService<IMemeGeneratorService>(services, CreateMockMemeGenerator());

            var mockImagen3 = CreateMockImagen3();
            ReplaceService<IImageGenerationService>(services, mockImagen3);
            // Same reason as the vision router above: the orchestrator now resolves image generation
            // through IImageGenerationRouter, never IImageGenerationService directly. Replacing only
            // the interface would leave the real ImageGenerationRouter in place, which hands back the
            // live GeminiImagen3Service and attempts a real network call.
            ReplaceService<IImageGenerationRouter>(services, new SingleImageGenerationRouter(mockImagen3));
        });

        return base.CreateHost(builder);
    }

    internal static void AddTestAuth(IServiceCollection services)
    {
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        services.PostConfigure<AuthenticationOptions>(options =>
        {
            options.DefaultScheme = TestAuthHandler.SchemeName;
            options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
            options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
        });
    }

    internal static void ReplaceService<T>(IServiceCollection services, T mockInstance) where T : class
    {
        // Singleton to match the real registrations (InfrastructureServiceExtensions). A scoped AI
        // mock would fail DI scope validation (a singleton cannot capture a scoped dependency).
        services.RemoveAll<T>();
        services.AddSingleton(mockInstance);
    }

    private static IVisionService CreateMockComputerVision()
    {
        var mock = new Mock<IVisionService>();
        mock.Setup(s => s.AnalyzeAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("A test description", (IReadOnlyList<string>)new List<string> { "cat", "animal", "pet" }, 0.92, 150L));
        return mock.Object;
    }

    private static IGenerativeAiService CreateMockOpenAI()
    {
        var mock = new Mock<IGenerativeAiService>();

        mock.Setup(s => s.EnhanceDescriptionAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("An enhanced detailed description of the image", 120, 250L));

        // NOTE: image generation moved off IGenerativeAiService onto IImageGenerationService — see CreateMockImagen3.
        mock.Setup(s => s.GenerateMemeCaptionAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("FUNNY TOP", "FUNNY BOTTOM", 50, 180L));

        return mock.Object;
    }

    private static IMemeGeneratorService CreateMockMemeGenerator()
    {
        var mock = new Mock<IMemeGeneratorService>();
        mock.Setup(s => s.GenerateMemeAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[] img, string _, string _, CancellationToken _) => (img, "image/png"));
        return mock.Object;
    }

    private static IImageGenerationService CreateMockImagen3()
    {
        // ImageRegeneration requires a configured Imagen3 service; the orchestrator throws otherwise.
        var mock = new Mock<IImageGenerationService>();
        mock.SetupGet(s => s.IsConfigured).Returns(true);
        mock.Setup(s => s.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x00 }, "image/png", 500L));
        return mock.Object;
    }
}

// ─── Service-failure tests ───────────────────────────────────────────────────

/// <summary>
/// Tests that verify the endpoint returns 500 when an upstream service throws,
/// not leaking raw stack traces through the API boundary.
/// </summary>
public class ImageAnalysisEndpointFailureTests : IClassFixture<ThrowingComputerVisionWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ImageAnalysisEndpointFailureTests(ThrowingComputerVisionWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task AnalyzeImage_WhenComputerVisionThrows_Returns500()
    {
        var validBase64 = Convert.ToBase64String(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x00 });
        var request = new ImageAnalysisRequest
        {
            ImageData = validBase64,
            ContentType = "image/png",
            Mode = ProcessingMode.ImageRegeneration,
            DescriptionLength = 200
        };

        var response = await _client.PostAsJsonAsync("/api/images/analyze", request);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task AnalyzeImage_WhenComputerVisionThrows_ReturnsJsonProblemDetails()
    {
        var validBase64 = Convert.ToBase64String(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00 });
        var request = new ImageAnalysisRequest
        {
            ImageData = validBase64,
            ContentType = "image/jpeg",
            Mode = ProcessingMode.MemeGeneration
        };

        var response = await _client.PostAsJsonAsync("/api/images/analyze", request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        // Should be ProblemDetails JSON, not a raw HTML error page
        using var doc = JsonDocument.Parse(content);
        Assert.True(doc.RootElement.TryGetProperty("title", out var title));
        Assert.Equal("Processing Error", title.GetString());
    }
}

/// <summary>
/// WebApplicationFactory where IComputerVisionService always throws HttpRequestException.
/// Used to verify the endpoint handles upstream failures gracefully (500, not crash).
/// </summary>
public class ThrowingComputerVisionWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment(PoEnvironments.Test);

        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["KeyVault:Uri"] = "",
                ["AZURE_KEY_VAULT_ENDPOINT"] = "",
                ["ComputerVision:Endpoint"] = "https://test.cognitiveservices.azure.com/",
                ["ComputerVision:ApiKey"] = "test-key",
                ["OpenAI:Endpoint"] = "https://test.openai.azure.com/",
                ["OpenAI:Key"] = "test-key",
                ["ApplicationInsights:ConnectionString"] = "",
                ["Storage:ConnectionString"] = "",
                ["Google:ApiKey"] = "test-key",
                ["Mocks:UseMockAi"] = "true"
            });
        });

        builder.ConfigureServices(services =>
        {
            MockedServicesWebApplicationFactory.AddTestAuth(services);

            // Vision (step 1 of the pipeline) always throws — proves the endpoint surfaces a clean 500.
            var throwingMock = new Mock<IVisionService>();
            throwingMock
                .Setup(s => s.AnalyzeAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("Simulated Azure CV outage"));
            MockedServicesWebApplicationFactory.ReplaceService<IVisionService>(services, throwingMock.Object);
            // Same reason as in MockedServicesWebApplicationFactory: the orchestrator goes through
            // the router, so replacing only IVisionService left the real AzureVisionService in play
            // and the test saw its 401 (surfaced as 503) instead of the simulated outage.
            MockedServicesWebApplicationFactory.ReplaceService<IVisionServiceRouter>(
                services, new SingleVisionServiceRouter(throwingMock.Object));

            // The orchestrator constructs all four AI services up front, so the remaining three must
            // resolve cleanly even though vision throws before they're invoked.
            MockedServicesWebApplicationFactory.ReplaceService<IGenerativeAiService>(services, Mock.Of<IGenerativeAiService>());
            var throwingTestImagen3 = Mock.Of<IImageGenerationService>();
            MockedServicesWebApplicationFactory.ReplaceService<IImageGenerationService>(services, throwingTestImagen3);
            // Same reason as the vision router above: the orchestrator resolves image generation
            // through IImageGenerationRouter, a constructor dependency built eagerly along with the
            // orchestrator — before vision even runs. Replacing only IImageGenerationService left the
            // real ImageGenerationRouter in play, which tried to construct GeminiImagen3Service and
            // tripped its Mocks:UseMockAi guard.
            MockedServicesWebApplicationFactory.ReplaceService<IImageGenerationRouter>(
                services, new SingleImageGenerationRouter(throwingTestImagen3));
            MockedServicesWebApplicationFactory.ReplaceService<IMemeGeneratorService>(services, Mock.Of<IMemeGeneratorService>());
        });

        return base.CreateHost(builder);
    }
}

// ─── Real per-request router seam ────────────────────────────────────────────
//
// Final whole-branch review, finding #1 (Critical): every WebApplicationFactory above replaces
// IImageGenerationRouter with SingleImageGenerationRouter, which always hands back one pre-picked
// service regardless of what ImageGenModelId says. That is exactly why the underlying defect —
// the client always sending an explicit Gemini id, silently making the ImageGen:Provider config
// flag unreachable for the main flow — survived six per-task reviews: no test anywhere composed
// "the request shape the client actually builds" (ImageGenModelId = null) with "the real
// ImageGenerationRouter resolving the ImageGen:Provider fallback". The factory below registers the
// real ImageGenerationRouter class (not a stub) over two distinguishable mock IImageGenerationService
// instances, so a regression that makes the client stamp an explicit id back onto the request would
// flip which marker bytes come back out — failing this test rather than passing unnoticed.

public class RealImageGenerationRouterTests : IClassFixture<RealImageGenRouterWebApplicationFactory>
{
    private readonly HttpClient _client;

    public RealImageGenerationRouterTests(RealImageGenRouterWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task AnalyzeImage_NullImageGenModelId_RealRouterUsesConfiguredProvider_NotGemini()
    {
        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var request = new ImageAnalysisRequest
        {
            ImageData = Convert.ToBase64String(imageBytes),
            ContentType = "image/png",
            Mode = ProcessingMode.ImageRegeneration,
            DescriptionLength = 200,
            // The exact shape FeaturePageBase.BuildAnalysisRequestAsync sends when nothing has been
            // explicitly picked and seeding did not override it — see AiSelectionState.GetExplicit.
            ImageGenModelId = null,
        };

        var response = await _client.PostAsJsonAsync("/api/images/analyze", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var regeneratedBytes = Convert.FromBase64String(
            doc.RootElement.GetProperty("regeneratedImageData").GetString()!);

        // ImageGen:Provider=huggingface is configured on this factory (matching both real
        // appsettings files); a null id must resolve through the REAL router to HuggingFace, never
        // fall through to Gemini.
        Assert.Equal(RealImageGenRouterWebApplicationFactory.HuggingFaceMarkerBytes, regeneratedBytes);
        Assert.NotEqual(RealImageGenRouterWebApplicationFactory.GeminiMarkerBytes, regeneratedBytes);
    }
}

/// <summary>
/// WebApplicationFactory that mirrors <see cref="MockedServicesWebApplicationFactory"/> for
/// everything except image generation, where it wires the REAL <see cref="ImageGenerationRouter"/>
/// (configured with <c>ImageGen:Provider=huggingface</c>) over two distinguishable mock
/// <see cref="IImageGenerationService"/> instances, instead of the usual
/// <see cref="SingleImageGenerationRouter"/> stub.
/// </summary>
public class RealImageGenRouterWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>Bytes the Gemini-arm mock returns — must never come back for a null model id.</summary>
    internal static readonly byte[] GeminiMarkerBytes = [0xEE, 0xEE, 0xEE, 0xEE];

    /// <summary>Bytes the HuggingFace-arm mock returns — the configured provider's expected output.</summary>
    internal static readonly byte[] HuggingFaceMarkerBytes = [0xAA, 0xAA, 0xAA, 0xAA];

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment(PoEnvironments.Test);

        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["KeyVault:Uri"] = "",
                ["AZURE_KEY_VAULT_ENDPOINT"] = "",
                ["ComputerVision:Endpoint"] = "https://test.cognitiveservices.azure.com/",
                ["ComputerVision:ApiKey"] = "test-key",
                ["OpenAI:Endpoint"] = "https://test.openai.azure.com/",
                ["OpenAI:Key"] = "test-key",
                ["ApplicationInsights:ConnectionString"] = "",
                ["Storage:ConnectionString"] = "",
                ["Google:ApiKey"] = "test-key",
                ["Mocks:UseMockAi"] = "true",
                // The value under test: both real appsettings files set this to huggingface today.
                ["ImageGen:Provider"] = "huggingface",
            });
        });

        builder.ConfigureServices(services =>
        {
            MockedServicesWebApplicationFactory.AddTestAuth(services);

            var mockVision = new Mock<IVisionService>();
            mockVision.Setup(s => s.AnalyzeAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(("A test description", (IReadOnlyList<string>)new List<string> { "cat" }, 0.92, 150L));
            MockedServicesWebApplicationFactory.ReplaceService<IVisionService>(services, mockVision.Object);
            MockedServicesWebApplicationFactory.ReplaceService<IVisionServiceRouter>(
                services, new SingleVisionServiceRouter(mockVision.Object));

            var mockOpenAi = new Mock<IGenerativeAiService>();
            mockOpenAi.Setup(s => s.EnhanceDescriptionAsync(
                    It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(("An enhanced detailed description of the image", 120, 250L));
            MockedServicesWebApplicationFactory.ReplaceService<IGenerativeAiService>(services, mockOpenAi.Object);

            // The two arms the real router picks between. Only their generated bytes differ, so a
            // test assertion on the response body proves which one actually ran.
            var geminiSpy = new Mock<IImageGenerationService>();
            geminiSpy.SetupGet(s => s.IsConfigured).Returns(true);
            geminiSpy.Setup(s => s.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((GeminiMarkerBytes, "image/png", 500L));

            var huggingFaceSpy = new Mock<IImageGenerationService>();
            huggingFaceSpy.SetupGet(s => s.IsConfigured).Returns(true);
            huggingFaceSpy.Setup(s => s.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((HuggingFaceMarkerBytes, "image/png", 500L));

            // The REAL router, not SingleImageGenerationRouter — this is the whole point of the test.
            // Reads configuredProvider from IConfiguration (the actual ImageGen:Provider key set
            // above) rather than a hardcoded literal, so this test is provably driven by that config
            // value and not merely decorative — flipping the in-memory value to "google" must flip
            // which marker bytes come back, not just be inert dead config.
            services.RemoveAll<IImageGenerationRouter>();
            services.AddSingleton<IImageGenerationRouter>(sp => new ImageGenerationRouter(
                geminiSpy.Object,
                huggingFaceSpy.Object,
                sp.GetRequiredService<IConfiguration>()[ConfigKeys.ImageGenProvider]!));
        });

        return base.CreateHost(builder);
    }
}
