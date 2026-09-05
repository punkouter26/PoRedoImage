using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Shared.Configuration;

namespace PoRedoImage.Infrastructure.Services;

/// <summary>
/// Resolves the image-generation service for a request. Google Gemini/Imagen is the only provider,
/// so every request routes to it whatever <paramref name="modelId"/> asks for.
/// </summary>
/// <remarks>
/// The type is kept rather than collapsed into a direct dependency because callers pass a
/// per-request model id and the indirection is where a second provider would slot back in. It had
/// a HuggingFace branch until 2026-08; see <c>InfrastructureServiceExtensions</c> for why that went.
/// </remarks>
public sealed class ImageGenerationRouter : IImageGenerationRouter
{
    private readonly IImageGenerationService _gemini;
    private readonly IImageGenerationService? _fastGemini;

    public ImageGenerationRouter(
        IImageGenerationService gemini,
        IImageGenerationService? fastGemini = null)
    {
        _gemini = gemini;
        _fastGemini = fastGemini;
    }

    public IImageGenerationService Resolve(string? modelId)
    {
        if (string.Equals(modelId, AiProviderIds.GeminiImagen3Fast, StringComparison.Ordinal) && _fastGemini is not null)
            return _fastGemini;

        return _gemini;
    }
}

/// <summary>
/// Router used when a single generation service should handle every request (e.g. mock mode).
/// </summary>
public sealed class SingleImageGenerationRouter(IImageGenerationService service) : IImageGenerationRouter
{
    public IImageGenerationService Resolve(string? modelId) => service;
}
