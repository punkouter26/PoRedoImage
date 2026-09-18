namespace PoRedoImage.Mobile.Services;

/// <summary>
/// Manages mobile client configuration and persistent preferences.
/// </summary>
public interface IMobileSettingsService
{
    string ServerUrl { get; set; }
    string GuestId { get; set; }
    string SelectedStyle { get; set; }
    bool AutoSaveToGallery { get; set; }
    string DefaultMode { get; set; }

    /// <summary>
    /// Route meme-caption text generation to the on-device model instead of the server.
    /// Off by default: the weights are side-loaded, so on a fresh install there is nothing to run.
    /// </summary>
    bool UseOnDeviceCaptions { get; set; }

    /// <summary>
    /// Which catalogued on-device model to load. Defaults to the smallest; bigger ones must be
    /// side-loaded first or the caption service reports them missing.
    /// </summary>
    string SelectedModelId { get; set; }

    /// <summary>
    /// Gate the gallery behind device biometrics. Only takes effect when the device has
    /// enrolled hardware credentials — the lock never strands a user without them.
    /// </summary>
    bool LockGalleryWithBiometrics { get; set; }

    /// <summary>
    /// Returns the resolved API base URI with trailing slash guaranteed.
    /// </summary>
    Uri GetBaseUri();
}

