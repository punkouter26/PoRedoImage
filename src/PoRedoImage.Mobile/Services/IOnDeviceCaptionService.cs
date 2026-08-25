namespace PoRedoImage.Mobile.Services;

/// <summary>
/// Generates meme captions on the phone, with no network call.
/// </summary>
public interface IOnDeviceCaptionService
{
    /// <summary>The model this service runs.</summary>
    OnDeviceModel Model { get; }

    /// <summary>
    /// Whether the weights are present. Re-probed on each call, so pushing the model with adb takes
    /// effect without reinstalling or restarting the app.
    /// </summary>
    OnDeviceModelStatus Probe();

    /// <summary>
    /// Writes a meme caption for a scene.
    /// </summary>
    /// <param name="sceneDescription">
    /// What the photo shows. Qwen2.5 is text-only, so this has to come from somewhere else — today
    /// that is the server's vision model.
    /// </param>
    /// <param name="stage">Receives coarse progress text for the UI.</param>
    /// <param name="ct">Cancels between generated tokens.</param>
    /// <exception cref="OnDeviceCaptionException">
    /// The model is missing, failed to load, or produced nothing usable. Never falls back to the
    /// server on its own.
    /// </exception>
    Task<string> GenerateMemeCaptionAsync(
        string sceneDescription,
        IProgress<string>? stage = null,
        CancellationToken ct = default);

    /// <summary>
    /// Releases the loaded weights. Worth calling when the app is backgrounded — the session holds
    /// several hundred megabytes of native memory that Android will otherwise count against the app
    /// when deciding what to kill.
    /// </summary>
    void Unload();
}
