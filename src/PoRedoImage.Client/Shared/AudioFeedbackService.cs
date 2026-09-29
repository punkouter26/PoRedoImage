using Microsoft.JSInterop;

namespace PoRedoImage.Client.Shared;

/// <summary>
/// The one C# door to the app's feedback layer: the synthesized cues in <c>wwwroot/js/audio.js</c>
/// plus the ticker-tape burst and haptics in <c>wwwroot/js/fx.js</c>.
/// Zero asset bytes: every cue is an <c>OscillatorNode</c> + filtered noise burst.
///
/// Honours <c>prefers-reduced-motion</c>, <c>prefers-reduced-data</c>, and a
/// <c>localStorage['poredoimage.audio.enabled']</c> kill switch — every call is a
/// safe no-op when the user has opted out.
///
/// Inject as scoped. Both scripts are loaded eagerly by the host page, so the globals exist.
/// </summary>
public sealed class AudioFeedbackService : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private readonly ILogger<AudioFeedbackService> _logger;

    public AudioFeedbackService(IJSRuntime js, ILogger<AudioFeedbackService> logger)
    {
        _js = js;
        _logger = logger;
    }

    /// <summary>
    /// Station chime, "Now arriving: <paramref name="label"/>", ticker-tape and a double tap —
    /// call on generation-complete. A null label skips the spoken line.
    /// </summary>
    public ValueTask SuccessAsync(string? label) => SafeInvoke("poFx.done", label);

    /// <summary>Low-passed noise burst and one long buzz — call on generation-failed / 4xx / 5xx.</summary>
    public ValueTask FailureAsync() => SafeInvoke("poFx.fail");

    /// <summary>Persist the user's audio opt-in/opt-out choice.</summary>
    public ValueTask SetEnabledAsync(bool enabled) => SafeInvoke("PoRedoImageAudio.setEnabled", enabled);

    private async ValueTask SafeInvoke(string method, params object?[] args)
    {
        try
        {
            await _js.InvokeVoidAsync(method, args);
        }
        catch (Exception ex)
        {
            // Audio is a polish layer — never let a failure here break a user flow.
            _logger.LogDebug(ex, "Audio cue '{Method}' was suppressed (no AudioContext or user opt-out).", method);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
