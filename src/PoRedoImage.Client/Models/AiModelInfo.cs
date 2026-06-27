namespace PoRedoImage.Client.Models;

/// <summary>
/// Represents an AI model available for selection in the Studio UI.
/// Models are categorised into Remote (Azure OpenAI), Web Browser, and Ollama (local).
/// </summary>
/// <remarks>
/// Strategy pattern (GoF): the model selection logic is encapsulated here so the UI stays
/// free of provider-specific details. The selected model is persisted in the user's session.
/// </remarks>
public sealed record AiModelInfo
{
    /// <summary>Display name shown in the dropdown (e.g. "GPT-4o").</summary>
    public required string DisplayName { get; init; }

    /// <summary>Internal identifier used for API routing (e.g. "gpt-4o", "gemini-2.0-flash").</summary>
    public required string ModelId { get; init; }

    /// <summary>Which category this model belongs to.</summary>
    public required AiModelCategory Category { get; init; }

    /// <summary>Short description shown as subtitle in the dropdown.</summary>
    public string? Description { get; init; }

    /// <summary>Whether this model is only available in Development mode (Ollama).</summary>
    public bool IsDevOnly { get; init; }
}

/// <summary>
/// Categories of AI models available in PoRedoImage.
/// </summary>
public enum AiModelCategory
{
    /// <summary>Remote cloud models (Azure OpenAI gpt-4o, Google Gemini, etc.)</summary>
    Remote,

    /// <summary>Web browser AI models that run client-side via WebGPU/WebAssembly</summary>
    WebBrowser,

    /// <summary>Ollama local models (dev only, requires Ollama running locally)</summary>
    Ollama
}

/// <summary>
/// Provides the full list of available AI models across all categories.
/// </summary>
/// <remarks>
/// Singleton pattern (GoF): the model catalog is fixed at compile time and shared across all users.
/// </remarks>
public static class AiModelCatalog
{
    /// <summary>Returns all available AI models.</summary>
    public static IReadOnlyList<AiModelInfo> All { get; } = new List<AiModelInfo>
    {
        // ── Remote (Azure OpenAI + Google) ──────────────────────────────
        new() { DisplayName = "GPT-4o", ModelId = "gpt-4o", Category = AiModelCategory.Remote, Description = "Azure OpenAI — Chat & image analysis" },
        new() { DisplayName = "Gemini 2.5 Flash Image", ModelId = "gemini-2.5-flash-image", Category = AiModelCategory.Remote, Description = "Google Gemini — Image generation" },

        // ── Web Browser ─────────────────────────────────────────────────
        new() { DisplayName = "WebNN Image Classifier", ModelId = "webnn-classifier", Category = AiModelCategory.WebBrowser, Description = "Client-side image classification via WebNN" },
        new() { DisplayName = "WebGPU Style Transfer", ModelId = "webgpu-style", Category = AiModelCategory.WebBrowser, Description = "Client-side neural style transfer via WebGPU" },

        // ── Ollama (Local — dev only) ───────────────────────────────────
        new() { DisplayName = "LLaMA 3.2 Vision", ModelId = "llama3.2-vision", Category = AiModelCategory.Ollama, Description = "Local vision model via Ollama", IsDevOnly = true },
        new() { DisplayName = "LLaVA", ModelId = "llava", Category = AiModelCategory.Ollama, Description = "Local multimodal model via Ollama", IsDevOnly = true },
    };

    /// <summary>Returns models filtered by category.</summary>
    public static IReadOnlyList<AiModelInfo> ByCategory(AiModelCategory category) =>
        All.Where(m => m.Category == category).ToList();

    /// <summary>Finds a model by its ModelId.</summary>
    public static AiModelInfo? Find(string? modelId) =>
        All.FirstOrDefault(m => m.ModelId == modelId);

    /// <summary>The category order shown in the model dropdown.</summary>
    public static readonly IReadOnlyList<AiModelCategory> CategoryOrder =
        [AiModelCategory.Remote, AiModelCategory.WebBrowser, AiModelCategory.Ollama];

    /// <summary>Human-readable group label for a category (used as the dropdown optgroup header).</summary>
    public static string Label(this AiModelCategory category) => category switch
    {
        AiModelCategory.Remote => "Remote (Azure)",
        AiModelCategory.WebBrowser => "Web Browser (Local)",
        AiModelCategory.Ollama => "Ollama (Local Machine)",
        _ => category.ToString(),
    };
}