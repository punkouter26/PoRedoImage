namespace PoRedoImage.Shared.DTOs;

/// <summary>
/// Processing mode for the image analysis pipeline.
/// Strategy pattern: determines which pipeline branch executes.
/// </summary>
public enum ProcessingMode
{
    ImageRegeneration = 0,
    MemeGeneration = 1
}
