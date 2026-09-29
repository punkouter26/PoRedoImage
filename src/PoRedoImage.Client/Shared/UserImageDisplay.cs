using PoRedoImage.Domain.Entities;

namespace PoRedoImage.Client.Shared;

/// <summary>
/// How a saved image is presented — shared by the My Images strip, the Gallery page and the
/// lightbox, which each used to carry their own copy of these (and the copies disagreed).
/// </summary>
public static class UserImageDisplay
{
    /// <summary>
    /// Bootstrap-icon class for the kind badge. Icons rather than emoji: emoji render in whatever
    /// colour and weight the platform font supplies, unreadable at the 16px badge size.
    /// </summary>
    public static string Icon(UserImageKind kind) => kind switch
    {
        UserImageKind.Regeneration => "bi-palette2",
        UserImageKind.Meme => "bi-chat-square-text",
        UserImageKind.BulkVariation => "bi-grid-3x3",
        _ => "bi-camera",
    };

    public static string FormatSize(long bytes) =>
        bytes < 1024 ? $"{bytes} B"
        : bytes < 1024 * 1024 ? $"{bytes / 1024.0:0.#} KB"
        : $"{bytes / (1024.0 * 1024.0):0.#} MB";
}
