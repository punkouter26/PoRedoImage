using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Infrastructure.Services;
using PoRedoImage.Shared.Configuration;

namespace PoRedoImage.Tests.Integration.Features;

/// <summary>
/// Guards the id-namespacing contract. The original router matched by bare prefix, so the browser
/// text model "qwen2.5-0.5b-instruct" resolved to Ollama — work would have gone to the wrong
/// backend the moment a browser id reached the server.
/// </summary>
/// <remarks>
/// Consolidated into a single [Theory]: TestCountCeilingTests counts fact/theory methods, not
/// InlineData cases, and this tier had only one method of headroom left (49/50) before this file
/// moved here from the Unit tier per the Task 1 brief's ceiling-overflow instructions.
/// </remarks>
public class VisionServiceRouterTests
{
    private static VisionServiceRouter BuildRouter(out AzureVisionService azure, out OllamaVisionService ollama)
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

        return new VisionServiceRouter(azure, ollama);
    }

    [Theory]
    [InlineData(AiProviderIds.OllamaVision, true)]
    // Regression: "qwen..." previously matched the Ollama prefix rule.
    [InlineData(AiProviderIds.BrowserQwen25, false)]
    [InlineData(null, false)]
    [InlineData("gemma4", false)]
    public void Resolve_RoutesByNamespace_NotByModelNamePrefix(string? modelId, bool expectOllama)
    {
        var router = BuildRouter(out var azure, out var ollama);
        IVisionService expected = expectOllama ? ollama : azure;

        Assert.Same(expected, router.Resolve(modelId));
    }
}
