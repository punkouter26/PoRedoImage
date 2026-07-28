namespace PoRedoImage.Domain.Interfaces;

/// <summary>
/// Selects the <see cref="IImageGenerationService"/> backend for a requested provider id.
/// Strategy pattern (GoF), mirroring <see cref="IVisionServiceRouter"/>.
/// </summary>
public interface IImageGenerationRouter
{
    /// <summary>
    /// Returns the image-generation service for the given id. Null or unrecognised ids fall back to
    /// the provider named by the <c>ImageGen:Provider</c> configuration flag.
    /// </summary>
    IImageGenerationService Resolve(string? modelId);
}
