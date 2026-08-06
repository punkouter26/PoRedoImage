using Moq;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Infrastructure.Services;
using PoRedoImage.Shared.Configuration;

namespace PoRedoImage.Tests.Unit.Features;

/// <summary>
/// Google Gemini/Imagen is the only image-generation provider, so every <c>Resolve</c> call returns
/// the Gemini service regardless of the caller-supplied id or any configured default. These tests
/// guard that, so adding a second provider back is an intentional code change rather than an
/// accidental widening. (A HuggingFace arm existed until 2026-08; see
/// <c>InfrastructureServiceExtensions</c> for why it was removed.)
/// </summary>
public class ImageGenerationRouterTests
{
    private static readonly IImageGenerationService Gemini = Mock.Of<IImageGenerationService>();

    private static ImageGenerationRouter Build() => new(Gemini);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(AiProviderIds.GeminiImagen3)]
    [InlineData("remote:hf-flux-schnell")]   // legacy id; router must NOT honour it
    [InlineData("remote:something-unknown")]
    public void Resolve_AlwaysReturnsGemini(string? modelId)
    {
        Assert.Same(Gemini, Build().Resolve(modelId));
    }
}