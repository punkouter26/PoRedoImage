using PoRedoImage.Domain.Interfaces;

namespace PoRedoImage.Infrastructure.Services;

/// <summary>
/// Routes the vision/analysis step to a local Ollama model when the selected model id is a known
/// local model, otherwise to the default Azure Computer Vision backend.
/// </summary>
public sealed class VisionServiceRouter(AzureVisionService azure, OllamaVisionService ollama) : IVisionServiceRouter
{
    public IVisionService Resolve(string? modelId) => IsLocalModel(modelId) ? ollama : azure;

    /// <summary>
    /// Local (Ollama) vision models. Matched by id so the UI catalog and this router stay decoupled.
    /// </summary>
    private static bool IsLocalModel(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return false;
        var id = modelId.Trim().ToLowerInvariant();
        return id.StartsWith("gemma", StringComparison.Ordinal)
            || id.StartsWith("llama", StringComparison.Ordinal)
            || id.StartsWith("llava", StringComparison.Ordinal)
            || id.StartsWith("qwen", StringComparison.Ordinal)
            || id.StartsWith("ollama", StringComparison.Ordinal);
    }
}

/// <summary>
/// Router used when a single vision service should handle every request (e.g. mock mode).
/// </summary>
public sealed class SingleVisionServiceRouter(IVisionService service) : IVisionServiceRouter
{
    public IVisionService Resolve(string? modelId) => service;
}
