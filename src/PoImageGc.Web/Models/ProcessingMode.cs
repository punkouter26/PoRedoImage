namespace PoImageGc.Web.Models;

/// <summary>
/// Defines the processing mode for image analysis.
/// Strategy pattern: determines which processing pipeline to execute.
/// </summary>
public enum ProcessingMode
{
    /// <summary>
    /// Original workflow: analyze image, generate description, regenerate image with DALL-E
    /// </summary>
    ImageRegeneration = 0,

    /// <summary>
    /// Meme generation: analyze image, generate funny caption, overlay text on original image
    /// </summary>
    MemeGeneration = 1
}
