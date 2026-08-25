namespace PoRedoImage.Mobile.Services;

/// <summary>
/// Persists app configuration in MAUI platform preferences.
/// </summary>
public class MobileSettingsService : IMobileSettingsService
{
    private const string ServerUrlKey = "poredo_server_url";
    private const string GuestIdKey = "poredo_guest_id";
    private const string StyleKey = "poredo_selected_style";
    private const string AutoSaveKey = "poredo_auto_save";
    private const string DefaultModeKey = "poredo_default_mode";
    private const string OnDeviceCaptionsKey = "poredo_on_device_captions";

    public string ServerUrl
    {
        get => Preferences.Default.Get(ServerUrlKey, GetDefaultServerUrl());
        set => Preferences.Default.Set(ServerUrlKey, value?.Trim().TrimEnd('/') ?? GetDefaultServerUrl());
    }

    public string GuestId
    {
        get
        {
            var id = Preferences.Default.Get(GuestIdKey, string.Empty);
            if (string.IsNullOrWhiteSpace(id))
            {
                id = $"GUEST{Random.Shared.Next(10000000, 99999999)}";
                Preferences.Default.Set(GuestIdKey, id);
            }
            return id;
        }
        set => Preferences.Default.Set(GuestIdKey, value);
    }

    public string SelectedStyle
    {
        get => Preferences.Default.Get(StyleKey, "Cyberpunk");
        set => Preferences.Default.Set(StyleKey, value);
    }

    public bool AutoSaveToGallery
    {
        get => Preferences.Default.Get(AutoSaveKey, false);
        set => Preferences.Default.Set(AutoSaveKey, value);
    }

    public string DefaultMode
    {
        get => Preferences.Default.Get(DefaultModeKey, "Meme");
        set => Preferences.Default.Set(DefaultModeKey, value);
    }

    public bool UseOnDeviceCaptions
    {
        get => Preferences.Default.Get(OnDeviceCaptionsKey, false);
        set => Preferences.Default.Set(OnDeviceCaptionsKey, value);
    }

    public Uri GetBaseUri()
    {
        var raw = ServerUrl;
        if (!raw.EndsWith('/'))
            raw += "/";

        if (Uri.TryCreate(raw, UriKind.Absolute, out var uri))
            return uri;

        return new Uri(GetDefaultServerUrl() + "/");
    }

    private static string GetDefaultServerUrl()
    {
        // On Android emulator, 10.0.2.2 maps to the host machine's localhost (where PoRedoImage.Web runs on port 4000)
        if (DeviceInfo.Current.Platform == DevicePlatform.Android && DeviceInfo.Current.DeviceType == DeviceType.Virtual)
            return "http://10.0.2.2:4000";

        return "http://localhost:4000";
    }
}

