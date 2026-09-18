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

    /// <summary>
    /// Same family, more parameters — noticeably better captions on ambiguous photos at the cost
    /// of roughly double the load time on a phone CPU. Side-loaded the same way.
    /// </summary>
    public static OnDeviceModel Qwen25Large { get; } = new(
        Id: "qwen2.5-1.5b-instruct",
        ProviderId: AiProviderIds.DeviceQwen25,
        DisplayName: "Qwen2.5 1.5B Instruct (int4)",
        ApproxBytes: 1_862_000_000L);

    /// <summary>
    /// Phi-3 mini — stronger instruction following; a good fit when captions keep drifting into
    /// answering the photo instead of captioning it.
    /// </summary>
    public static OnDeviceModel Phi3Mini { get; } = new(
        Id: "phi-3-mini-4k-instruct",
        ProviderId: AiProviderIds.DeviceQwen25,
        DisplayName: "Phi-3 mini 4K Instruct (int4)",
        ApproxBytes: 2_341_000_000L);

    /// <summary>
    /// Llama 3.2 3B — the biggest option catalogued here. Better prose, slowest generation on
    /// mid-range hardware; users opt in deliberately.
    /// </summary>
    public static OnDeviceModel Llama32Three { get; } = new(
        Id: "llama-3.2-3b-instruct",
        ProviderId: AiProviderIds.DeviceQwen25,
        DisplayName: "Llama 3.2 3B Instruct (int4)",
        ApproxBytes: 2_190_000_000L);

    /// <summary>
    /// Every model the caption service can execute, in the order Settings lists them. All carry
    /// the same provider id: selection is by side-loaded weights, not by provider routing.
    /// </summary>
    public static IReadOnlyList<OnDeviceModel> All { get; } =
    [
        Qwen25MemeCaption,
        Qwen25Large,
        Phi3Mini,
        Llama32Three,
    ];
}
