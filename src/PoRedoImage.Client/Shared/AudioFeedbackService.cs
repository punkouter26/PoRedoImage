using Microsoft.JSInterop;

namespace PoRedoImage.Client.Shared;

/// <summary>
/// Thin wrapper over the procedurally-synthesized audio cues in <c>wwwroot/js/audio.js</c>.
/// Zero asset bytes: every cue is an <c>OscillatorNode</c> + lowpass-filtered noise burst.
///
/// Honours <c>prefers-reduced-motion</c>, <c>prefers-reduced-data</c>, and a
/// <c>localStorage['poredoimage.audio.enabled']</c> kill switch — every call is a
/// safe no-op when the user has opted out.
///
/// Inject as scoped. Resolves the <c>window.PoRedoImageAudio</c> global lazily on
/// first use; the JS module is loaded eagerly via <c>Program.cs</c> script registration
/// so the global is always present.
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

    /// <summary>Two-note success arpeggio (A5 → E6) — call on generation-complete.</summary>
    public ValueTask SuccessAsync() => SafeInvoke("success");

    /// <summary>Low-passed noise burst — call on generation-failed / 4xx / 5xx.</summary>
    public ValueTask FailureAsync() => SafeInvoke("failure");

    /// <summary>Single soft tick — call on micro-state changes (button press, etc.).</summary>
    public ValueTask TickAsync() => SafeInvoke("tick");

    /// <summary>Persist the user's audio opt-in/opt-out choice.</summary>
    public ValueTask SetEnabledAsync(bool enabled) => SafeInvoke("setEnabled", enabled);

    private async ValueTask SafeInvoke(string method, params object?[] args)
    {
        try
        {
            await _js.InvokeVoidAsync($"PoRedoImageAudio.{method}", args);
        }
        catch (Exception ex)
        {
            // Audio is a polish layer — never let a failure here break a user flow.
            _logger.LogDebug(ex, "Audio cue '{Method}' was suppressed (no AudioContext or user opt-out).", method);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
