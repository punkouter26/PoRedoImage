namespace PoRedoImage.Mobile.Services;

/// <summary>
/// Service interface for native sharing and local saving of generated results.
/// </summary>
public interface IShareService
{
    /// <summary>
    /// Opens the native system share sheet with an image file.
    /// </summary>
    Task ShareImageAsync(byte[] imageBytes, string fileName, string title = "PoRedo Image");

    /// <summary>
    /// Opens the native system share sheet with text (e.g. rap roast lyrics).
    /// </summary>
    Task ShareTextAsync(string text, string title = "PoRedo Roast");

    /// <summary>
    /// Saves the image to the device's local photo cache or public storage folder.
    /// </summary>
    Task<string?> SaveToDeviceAsync(byte[] imageBytes, string fileName);
}

