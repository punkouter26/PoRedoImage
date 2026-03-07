namespace PoRedoImage.Web.Features.ImageSession;

/// <summary>
/// Per-circuit (scoped) service that persists the active image across all feature pages.
/// Users can navigate between Regeneration, Meme Generation, and Bulk Generate without re-uploading.
/// </summary>
public sealed class ImageSessionService
{
    public string? PreviewUrl { get; private set; }
    public byte[]? Bytes { get; private set; }
    public string? ContentType { get; private set; }
    public string? FileName { get; private set; }
    public bool HasImage => PreviewUrl is not null;

    /// <summary>Raised whenever the active image changes (set or cleared).</summary>
    public event Action? OnChange;

    public void SetImage(string previewUrl, string contentType, string? fileName, byte[]? bytes = null)
    {
        PreviewUrl = previewUrl;
        ContentType = contentType;
        FileName = fileName;
        Bytes = bytes;
        OnChange?.Invoke();
    }

    public void Clear()
    {
        PreviewUrl = null;
        ContentType = null;
        FileName = null;
        Bytes = null;
        OnChange?.Invoke();
    }
}
