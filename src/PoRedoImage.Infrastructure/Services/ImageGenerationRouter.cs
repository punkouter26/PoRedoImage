using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Shared.Configuration;

namespace PoRedoImage.Infrastructure.Services;

/// <summary>
/// Routes image generation to Gemini/Imagen or HuggingFace per request.
/// </summary>
/// <remarks>
/// The <c>ImageGen:Provider</c> flag remains the fallback rather than being replaced, so a request
/// that carries no id behaves exactly as it did before per-request routing existed. That keeps the
/// no-redeploy config flip working and makes this change additive.
/// </remarks>
public sealed class ImageGenerationRouter(
    IImageGenerationService gemini) : IImageGenerationRouter
{
    public IImageGenerationService Resolve(string? modelId) => gemini;
}

/// <summary>
/// Router used when a single generation service should handle every request (e.g. mock mode).
/// </summary>
public sealed class SingleImageGenerationRouter(IImageGenerationService service) : IImageGenerationRouter
{
    public IImageGenerationService Resolve(string? modelId) => service;
}
