using Moq;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Infrastructure.Services;
using PoRedoImage.Shared.Configuration;

namespace PoRedoImage.Tests.Unit.Features;

/// <summary>
/// 2026-07: HuggingFace fal-ai image generation is broken on the upstream provider (every POST to
/// <c>fal-ai/{flux/schnell,qwen-image-edit}</c> returns HTTP 400). The router is therefore pinned
/// to Gemini: every <c>Resolve</c> call returns the Gemini service regardless of the caller-supplied
/// id or any configured default. These tests guard the pinning so a future swap (e.g. when fal-ai
/// restores routing) is an intentional code change rather than an accidental widening.
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