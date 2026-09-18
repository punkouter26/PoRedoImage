using PoRedoImage.Mobile.Services;

namespace PoRedoImage.Mobile.Platforms.Android;

/// <summary>
/// Bridges the static share-intent store (fed by <see cref="MainActivity"/>) into DI so the
/// shared ViewModel can consume shared photos without referencing Android types.
/// </summary>
public sealed class AndroidSharedImageInbox : ISharedImageInbox
{
    public bool TryTake(out byte[] bytes, out string fileName, out string contentType)
    {
        var item = SharedImageInbox.Consume();
        if (item is null)
        {
            bytes = [];
            fileName = string.Empty;
            contentType = string.Empty;
            return false;
        }

        bytes = item.Value.Bytes;
        fileName = item.Value.FileName;
        contentType = "image/jpeg";
        return true;
    }
}
