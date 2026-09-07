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
///
/// It also had a "fast tier" branch returning a second Gemini instance for
/// <c>remote:gemini-imagen3-fast</c>. That branch was unreachable: the fast instance was only
/// constructed when <c>Google:Imagen3FastModel</c> was configured, and that key was set nowhere in
/// the repo or the deployed app settings, so the branch's null guard always fell through to the
/// standard service while the picker advertised a lower price. Re-add it together with the config,
/// or not at all.
/// </remarks>
public sealed class ImageGenerationRouter(IImageGenerationService gemini) : IImageGenerationRouter
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
