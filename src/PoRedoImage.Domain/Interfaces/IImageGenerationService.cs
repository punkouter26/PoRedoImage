namespace PoRedoImage.Domain.Interfaces;

/// <summary>
/// Vendor-agnostic domain abstraction for text-to-image and image-to-image generation.
/// Kept separate from IGenerativeAiService so the image-generation provider (currently Gemini /
/// Imagen 3, see GeminiImagen3Service) can be mocked and swapped independently of the chat model.
/// </summary>
public interface IImageGenerationService
{
    Task<(byte[] ImageData, string ContentType, long ElapsedMs)>
        GenerateAsync(string prompt, CancellationToken ct = default);

    Task<(byte[] ImageData, string ContentType, long ElapsedMs)>
        GenerateImageAsync(string prompt, byte[] imageBytes, CancellationToken ct = default);

    /// <summary>
    /// Generates a single image-to-image variation, optionally with a deterministic seed
    /// offset so parallel re-rolls produce different outputs.
    /// </summary>
    /// <param name="seed">Non-negative integer; passed to the upstream model as a seed hint.</param>
    Task<(byte[] ImageData, string ContentType, long ElapsedMs)>
        GenerateImageAsync(string prompt, byte[] imageBytes, int seed, CancellationToken ct = default);

    bool IsConfigured { get; }
}
