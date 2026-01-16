namespace PoImageGc.Web.Features.ImageAnalysis;

/// <summary>
/// Null implementation of meme generator for non-Windows platforms
/// </summary>
public class NullMemeGeneratorService : IMemeGeneratorService
{
    private readonly ILogger<NullMemeGeneratorService> _logger;

    public NullMemeGeneratorService(ILogger<NullMemeGeneratorService> logger)
    {
        _logger = logger;
    }

    public byte[] AddCaptionToImage(byte[] imageData, string? topText, string? bottomText)
    {
        _logger.LogWarning("Meme generation is not supported on this platform. Returning original image.");
        
        // Return original image without modification
        return imageData;
    }
}
