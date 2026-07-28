using Moq;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Infrastructure.Services;
using PoRedoImage.Shared.Configuration;

namespace PoRedoImage.Tests.Unit.Features;

/// <summary>
/// The router must never widen behaviour for callers that send nothing: a null or unrecognised id
/// has to resolve exactly as the ImageGen:Provider flag already did, or flipping the flag in config
/// would silently stop working.
/// </summary>
public class ImageGenerationRouterTests
{
    private static readonly IImageGenerationService Gemini = Mock.Of<IImageGenerationService>();
    private static readonly IImageGenerationService HuggingFace = Mock.Of<IImageGenerationService>();

    private static ImageGenerationRouter Build(string configuredDefault) =>
        new(Gemini, HuggingFace, configuredDefault);

    [Fact]
    public void Resolve_HuggingFaceId_ReturnsHuggingFace()
    {
        Assert.Same(HuggingFace, Build("google").Resolve(AiProviderIds.HuggingFaceFlux));
    }

    [Fact]
    public void Resolve_GeminiId_ReturnsGemini()
    {
        Assert.Same(Gemini, Build("huggingface").Resolve(AiProviderIds.GeminiImagen3));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("remote:something-unknown")]
    public void Resolve_NullOrUnknownId_FallsBackToConfiguredProvider(string? modelId)
    {
        Assert.Same(HuggingFace, Build("huggingface").Resolve(modelId));
        Assert.Same(Gemini, Build("google").Resolve(modelId));
    }
}
