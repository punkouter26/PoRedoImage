using PoRedoImage.Shared.Configuration;

namespace PoRedoImage.Mobile.Services;

/// <summary>
/// One model the app can execute natively on the phone.
/// </summary>
/// <param name="Id">
/// Directory name under the models root <em>and</em> the suffix of <paramref name="ProviderId"/>.
/// <c>SCRIPTS/push-mobile-model.ps1</c> pushes to exactly this name; renaming it here without
/// renaming it there strands the weights where nothing looks for them.
/// </param>
/// <param name="ProviderId">Namespaced id from <see cref="AiProviderIds"/>.</param>
/// <param name="DisplayName">Name shown in Settings.</param>
/// <param name="ApproxBytes">Total on-disk size, shown when the model is missing.</param>
public sealed record OnDeviceModel(
    string Id,
    string ProviderId,
    string DisplayName,
    long ApproxBytes);

/// <summary>
/// The catalog of natively-executed models. Mirrors <c>LocalModelRegistry</c> on the web client —
/// same idea, different execution location, so deliberately not shared: the browser registry
/// carries WebGPU dtype chains that mean nothing to ONNX Runtime GenAI.
/// </summary>
public static class OnDeviceModelCatalog
{
    /// <summary>
    /// Qwen2.5 0.5B Instruct, int4-quantized for CPU. Writes the meme caption from a scene
    /// description; it is text-only, so the description itself still comes from a vision model.
    /// </summary>
    public static OnDeviceModel Qwen25MemeCaption { get; } = new(
        Id: "qwen2.5-0.5b-instruct",
        ProviderId: AiProviderIds.DeviceQwen25,
        DisplayName: "Qwen2.5 0.5B Instruct (int4)",
        ApproxBytes: 833_849_001L);
}
