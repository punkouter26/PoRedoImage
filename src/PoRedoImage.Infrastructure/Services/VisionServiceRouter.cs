using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Shared.Configuration;

namespace PoRedoImage.Infrastructure.Services;

/// <summary>
/// Routes the vision/analysis step to the local Ollama service when the selected id is
/// <c>ollama:</c>-namespaced, otherwise to the default Azure Computer Vision backend.
/// </summary>
/// <remarks>
/// Matching is an explicit namespace check, not a model-name prefix guess. The previous rule
/// treated any id starting with "qwen" as Ollama, which collides with the browser text model
/// <c>browser:qwen2.5-0.5b-instruct</c>. Browser ids resolve to Azure here because a browser
/// selection is executed client-side and should never have reached the server at all — falling back
/// to the default backend is the safe reading of an id this router should not have seen.
/// </remarks>
public sealed class VisionServiceRouter(AzureVisionService azure, OllamaVisionService ollama) : IVisionServiceRouter
{
    public IVisionService Resolve(string? modelId) =>
        AiProviderIds.IsOllama(modelId) ? ollama : azure;
}

/// <summary>
/// Router used when a single vision service should handle every request (e.g. mock mode).
/// </summary>
public sealed class SingleVisionServiceRouter(IVisionService service) : IVisionServiceRouter
{
    public IVisionService Resolve(string? modelId) => service;
}
