namespace PoRedoImage.Domain.Interfaces;

/// <summary>
/// Domain service interface for image-generation via Imagen 3.
/// Separate from IGenerativeAiService to allow independent mocking and registration.
/// </summary>
public interface IImagen3Service
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
