using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Infrastructure.Services;
using PoRedoImage.Shared.Configuration;

namespace PoRedoImage.Tests.Unit.Features;

/// <summary>
/// Guards the id-namespacing contract. The original router matched by bare prefix, so the browser
/// text model "qwen2.5-0.5b-instruct" resolved to Ollama — work would have gone to the wrong
/// backend the moment a browser id reached the server.
/// </summary>
public class VisionServiceRouterTests
{
    private static VisionServiceRouter BuildRouter(
        out AzureVisionService azure, out OllamaVisionService ollama, out OpenAiVisionService openAi)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ComputerVision:Endpoint"] = "https://test.cognitiveservices.azure.com/",
            ["ComputerVision:ApiKey"] = "test-key",
            ["Ollama:Endpoint"] = "http://localhost:11434",
        }).Build();

        azure = new AzureVisionService(config, Mock.Of<ILogger<AzureVisionService>>());

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());
        ollama = new OllamaVisionService(factory.Object, config, Mock.Of<ILogger<OllamaVisionService>>());

        var chat = new Mock<IChatCompletionService>();
        chat.SetupGet(c => c.IsConfigured).Returns(true);
        openAi = new OpenAiVisionService(chat.Object, Mock.Of<ILogger<OpenAiVisionService>>());

        return new VisionServiceRouter(
            azure, ollama, openAi,
            new MemoryCache(new MemoryCacheOptions()),
            new LoggerFactory());
    }

    [Theory]
    [InlineData(AiProviderIds.OllamaVision, "ollama")]
    [InlineData(AiProviderIds.AzureOpenAiVision, "openai")]
    // Regression: "qwen..." previously matched the Ollama prefix rule.
    [InlineData(AiProviderIds.BrowserQwen25, "azure")]
    [InlineData(null, "azure")]
    [InlineData("gemma4", "azure")]
    public void Resolve_RoutesByNamespace_NotByModelNamePrefix(string? modelId, string expectedBackend)
    {
        var router = BuildRouter(out var azure, out var ollama, out var openAi);
        IVisionService expected = expectedBackend switch
        {
            "ollama" => ollama,
            "openai" => openAi,
            _ => azure,
        };

        // Each backend is wrapped in its own cache decorator, so the routed instance is the wrapper
        // and the assertion has to look through it.
        var resolved = Assert.IsType<CachingVisionService>(router.Resolve(modelId));
        Assert.Same(expected, resolved.Inner);
    }
}
