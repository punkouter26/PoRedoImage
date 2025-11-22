namespace Server.Services;

/// <summary>
/// Interface for meme generation service operations
/// </summary>
public interface IMemeGeneratorService
{
    /// <summary>
    /// Adds meme-style caption overlay to an image
    /// </summary>
    /// <param name="imageData">Original image data as byte array</param>
    /// <param name="topText">Text to display at the top (can be null or empty)</param>
    /// <param name="bottomText">Text to display at the bottom (can be null or empty)</param>
    /// <returns>Modified image data with caption overlay as byte array</returns>
    byte[] AddCaptionToImage(byte[] imageData, string? topText, string? bottomText);
}
