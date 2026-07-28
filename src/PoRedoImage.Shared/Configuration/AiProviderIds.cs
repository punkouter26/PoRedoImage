namespace PoRedoImage.Shared.Configuration;

/// <summary>
/// The namespaced provider-id vocabulary shared by the client catalog and the server routers.
/// </summary>
/// <remarks>
/// Ids are namespaced by execution location — <c>remote:</c>, <c>ollama:</c>, <c>browser:</c> —
/// because the previous scheme matched bare model-name prefixes and could not tell a browser model
/// apart from an Ollama one. Const strings only: this type is consumed by the trim-analysed
/// <c>.Shared</c> assembly.
/// </remarks>
public static class AiProviderIds
{
    public const string RemotePrefix = "remote:";
    public const string OllamaPrefix = "ollama:";
    public const string BrowserPrefix = "browser:";

    // Remote (hosted APIs)
    public const string AzureComputerVision = "remote:azure-cv";
    public const string AzureOpenAi = "remote:azure-openai";
    public const string GeminiImagen3 = "remote:gemini-imagen3";
    public const string HuggingFaceChat = "remote:hf-chat";
    public const string GoogleLyria = "remote:google-lyria";

    // Ollama (local service, dev only)
    public const string OllamaVision = "ollama:vision";

    // Browser (WebGPU / WebAssembly, executed client-side)
    public const string BrowserFlorence2 = "browser:florence2-base";

    /// <summary>
    /// Browser text model. Not currently offered in the catalog — browser-local text enhancement is
    /// unimplemented — but defined here because <c>VisionServiceRouter</c> must provably not mistake
    /// it for an Ollama id.
    /// </summary>
    public const string BrowserQwen25 = "browser:qwen2.5-0.5b-instruct";

    /// <summary>True when the id names the local Ollama service.</summary>
    public static bool IsOllama(string? id) =>
        id?.StartsWith(OllamaPrefix, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>True when the id names a model that executes in the browser.</summary>
    public static bool IsBrowser(string? id) =>
        id?.StartsWith(BrowserPrefix, StringComparison.OrdinalIgnoreCase) == true;
}
