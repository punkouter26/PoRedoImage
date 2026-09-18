namespace PoRedoImage.Mobile.Services;

/// <summary>
/// Fallback audio player for non-Android platforms or test environments.
/// </summary>
public class NullAudioPlayer : IAudioPlayerService
{
    public bool IsPlaying => false;

    public event EventHandler? PlaybackStarted;
    public event EventHandler? PlaybackEnded;
    public event EventHandler<string>? PlaybackError;

    public Task PlayAsync(byte[] audioBytes, string contentType = "audio/mpeg")
    {
        return Task.CompletedTask;
    }

    public void Pause()
    {
    }

    public void Resume()
    {
    }

    public void Stop()
    {
        PlaybackEnded?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
