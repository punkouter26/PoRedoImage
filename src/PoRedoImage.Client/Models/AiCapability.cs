namespace PoRedoImage.Client.Models;

/// <summary>
/// A distinct AI job the app performs. One selector is rendered per value.
/// </summary>
public enum AiCapability
{
    /// <summary>Image in, description + tags out (<c>IVisionService</c>).</summary>
    AnalyzeImage = 0,

    /// <summary>Prompt in, image out (<c>IImageGenerationService</c>).</summary>
    GenerateImage = 1,

    /// <summary>
    /// Description enhancement and meme captions (<c>IGenerativeAiService</c>). Both methods share
    /// one implementation and one provider, so they share one selector.
    /// </summary>
    EnhanceDescription = 2,

    /// <summary>Style Director reasoning agents (<c>IChatCompletionService</c>).</summary>
    StyleDirector = 3,

    /// <summary>OCR, dense captions, objects (<c>ISceneDetailProvider</c>).</summary>
    SceneDetail = 4,

    /// <summary>Lyrics in, performed track out (<c>IMusicGenerationService</c>).</summary>
    CreateAudio = 5,
}
