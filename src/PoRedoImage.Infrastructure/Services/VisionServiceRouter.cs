using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Shared.Configuration;

namespace PoRedoImage.Infrastructure.Services;

/// <summary>
/// Routes the vision/analysis step to the backend the caller selected.
/// </summary>
/// <remarks>
/// <para>
/// Matching is an explicit namespace check, not a model-name prefix guess. The previous rule
/// treated any id starting with "qwen" as Ollama, which collides with the browser text model
/// <c>browser:qwen2.5-0.5b-instruct</c>. Browser ids resolve to the default here because a browser
/// selection is executed client-side and should never have reached the server at all — falling back
/// to the default backend is the safe reading of an id this router should not have seen.
/// </para>
/// <para>
/// Each backend is wrapped in its own <see cref="CachingVisionService"/>, built once in the
/// constructor. Per-backend wrappers matter: the cache key is the image content hash, and two
/// backends answer the same question differently, so sharing one wrapper would let an Ollama answer
/// be served to a caller who asked for Azure.
/// </para>
/// </remarks>
public sealed class VisionServiceRouter : IVisionServiceRouter
{
    private readonly IVisionService _azure;
    private readonly IVisionService _ollama;
    private readonly IVisionService _openAi;
    private readonly IVisionService? _gemini;

    public VisionServiceRouter(
        AzureVisionService azure,
        OllamaVisionService ollama,
        OpenAiVisionService openAi,
        IMemoryCache cache,
        ILoggerFactory loggerFactory,
        GeminiVisionService? gemini = null)
    {
        var log = loggerFactory.CreateLogger<CachingVisionService>();
        _azure = new CachingVisionService(azure, cache, log, "vision:azure-cv");
        _ollama = new CachingVisionService(ollama, cache, log, "vision:ollama");
        _openAi = new CachingVisionService(openAi, cache, log, "vision:openai");
        if (gemini is not null)
            _gemini = new CachingVisionService(gemini, cache, log, "vision:gemini");
    }

    public IVisionService Resolve(string? modelId)
    {
        if (AiProviderIds.IsOllama(modelId)) return _ollama;
        if (string.Equals(modelId, AiProviderIds.AzureOpenAiVision, StringComparison.Ordinal)) return _openAi;
        if (string.Equals(modelId, AiProviderIds.GeminiVision, StringComparison.Ordinal) && _gemini is not null) return _gemini;
        return _azure;
    }
}

/// <summary>
/// Router used when a single vision service should handle every request (e.g. mock mode).
/// </summary>
public sealed class SingleVisionServiceRouter(IVisionService service) : IVisionServiceRouter
{
    public IVisionService Resolve(string? modelId) => service;
}
