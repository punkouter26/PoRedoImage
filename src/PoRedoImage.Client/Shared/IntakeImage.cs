namespace PoRedoImage.Client.Shared;

/// <summary>
/// An image that reached the app without going through <c>&lt;InputFile&gt;</c> — a clipboard paste
/// (Ctrl+V) or a drop that landed outside the upload panel's own drop zone.
/// </summary>
/// <remarks>
/// Populated by <c>wwwroot/js/ux.js</c> via <c>OnImageIntake</c>. The JS side applies the same
/// type/size validation as <see cref="ImageLoadHelper"/> and reports a failure through
/// <see cref="Error"/> rather than throwing, so a rejected paste is a message, not an exception.
/// Exactly one of <see cref="Base64"/> / <see cref="Error"/> is non-null.
/// </remarks>
public sealed record IntakeImage
{
    /// <summary>Raw base64 payload (no <c>data:</c> prefix). Null when <see cref="Error"/> is set.</summary>
    public string? Base64 { get; init; }

    public string? ContentType { get; init; }

    public string? FileName { get; init; }

    /// <summary><c>"paste"</c> or <c>"drop"</c> — used only to word the confirmation toast.</summary>
    public string? Source { get; init; }

    /// <summary>User-facing rejection reason (wrong type, too large, unreadable), or null on success.</summary>
    public string? Error { get; init; }

    /// <summary>Decoded bytes, or null when the payload is missing or not valid base64.</summary>
    public byte[]? Decode()
    {
        if (string.IsNullOrEmpty(Base64)) return null;
        try { return Convert.FromBase64String(Base64); }
        catch (FormatException) { return null; }
    }
}
