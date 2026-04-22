namespace PoRedoImage.Domain.Interfaces;

/// <summary>
/// Domain service interface for meme image generation (ImageSharp overlay).
/// </summary>
public interface IMemeGeneratorService
{
    Task<(byte[] ImageData, string ContentType)>
        GenerateMemeAsync(byte[] sourceImage, string topText, string bottomText, CancellationToken ct = default);
}
