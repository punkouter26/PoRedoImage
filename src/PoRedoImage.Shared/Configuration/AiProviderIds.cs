namespace PoRedoImage.Shared.Configuration;

/// <summary>
/// The namespaced provider-id vocabulary shared by the client catalog and the server routers.
/// </summary>
/// <remarks>
/// Ids are namespaced by execution location — <c>remote:</c>, <c>ollama:</c>, <c>browser:</c>,
/// <c>device:</c> — because the previous scheme matched bare model-name prefixes and could not tell
/// a browser model apart from an Ollama one. Const strings only: this type is consumed by the
/// trim-analysed <c>.Shared</c> assembly.
/// </remarks>
public static class AiProviderIds
{
    public const string RemotePrefix = "remote:";
    public const string OllamaPrefix = "ollama:";
    public const string BrowserPrefix = "browser:";

    /// <summary>
    /// Native execution inside the MAUI head, on the phone's own CPU. Distinct from
    /// <see cref="BrowserPrefix"/>: the same weights can exist in both namespaces, but the runtime,
    /// the packaging, and the failure modes are entirely different, so nothing may treat one as the
    /// other.
    /// </summary>
    public const string DevicePrefix = "device:";

    // Remote (hosted APIs)
    public const string AzureComputerVision = "remote:azure-cv";
    public const string AzureOpenAi = "remote:azure-openai";

    /// <summary>
    /// Vision analysis performed by the chat deployment instead of Computer Vision — one call that
    /// returns a real caption plus tags. See <c>OpenAiVisionService</c> for why that is worth an id
    /// of its own rather than being folded into <see cref="AzureOpenAi"/>: it is a different
    /// capability on the same resource, and the picker offers them separately.
    /// </summary>
    public const string AzureOpenAiVision = "remote:azure-openai-vision";
    public const string GeminiImagen3 = "remote:gemini-imagen3";
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

    // Device (ONNX Runtime GenAI, executed natively by the MAUI head)

    /// <summary>
    /// Meme-caption text generation on the phone itself. Same base weights as
    /// <see cref="BrowserQwen25"/>, but an int4 ONNX Runtime GenAI build rather than an MLC one —
    /// side-loaded to app storage by <c>SCRIPTS/push-mobile-model.ps1</c>, never bundled in the APK.
    /// </summary>
    public const string DeviceQwen25 = "device:qwen2.5-0.5b-instruct";

    /// <summary>True when the id names the local Ollama service.</summary>
    public static bool IsOllama(string? id) =>
        id?.StartsWith(OllamaPrefix, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>True when the id names a model that executes in the browser.</summary>
    public static bool IsBrowser(string? id) =>
        id?.StartsWith(BrowserPrefix, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>True when the id names a model that executes natively on the device.</summary>
    public static bool IsDevice(string? id) =>
        id?.StartsWith(DevicePrefix, StringComparison.OrdinalIgnoreCase) == true;
}
