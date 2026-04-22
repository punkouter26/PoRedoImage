namespace PoRedoImage.Web.Components.Shared;

/// <summary>
/// Per-circuit (scoped) service that persists the active image across all feature pages.
/// Users can navigate between Regeneration, Meme Generation, and Bulk Generate without re-uploading.
/// Memory: only raw bytes are stored; PreviewUrl is computed on demand with lazy caching — avoids
/// holding both the byte array and an equally-sized base64 string simultaneously.
/// </summary>
public sealed class ImageSessionService
{
    private string? _cachedPreviewUrl;

    /// <summary>Lazily-computed data-URI from <see cref="Bytes"/> and <see cref="ContentType"/>.</summary>
    public string? PreviewUrl =>
        Bytes != null && ContentType != null
            ? (_cachedPreviewUrl ??= $"data:{ContentType};base64,{Convert.ToBase64String(Bytes)}")
            : null;

    public byte[]? Bytes { get; private set; }
    public string? ContentType { get; private set; }
    public string? FileName { get; private set; }
    public bool HasImage => Bytes is not null;

    /// <summary>Raised whenever the active image changes (set or cleared).</summary>
    public event Action? OnChange;

    /// <summary>
    /// Sets the active image. If <paramref name="bytes"/> is null and <paramref name="previewUrl"/> is a
    /// data-URI, the bytes are parsed from the URI so only one copy is held in memory.
    /// </summary>
    public void SetImage(string? previewUrl, string contentType, string? fileName, byte[]? bytes = null)
    {
        if (bytes is null && previewUrl is not null)
        {
            var commaIdx = previewUrl.IndexOf(',');
            if (commaIdx >= 0)
            {
                try { bytes = Convert.FromBase64String(previewUrl[(commaIdx + 1)..]); }
                catch { /* previewUrl is not a data-URI — bytes remain null */ }
            }
        }

        Bytes = bytes;
        ContentType = contentType;
        FileName = fileName;
        _cachedPreviewUrl = null; // invalidate cache on every update
        OnChange?.Invoke();
    }

    public void Clear()
    {
        Bytes = null;
        ContentType = null;
        FileName = null;
        _cachedPreviewUrl = null;
        OnChange?.Invoke();
    }
}
