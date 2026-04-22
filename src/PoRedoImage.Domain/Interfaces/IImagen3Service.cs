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

    bool IsConfigured { get; }
}
