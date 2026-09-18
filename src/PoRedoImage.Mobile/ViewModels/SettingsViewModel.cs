using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoRedoImage.Mobile.Services;

namespace PoRedoImage.Mobile.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IMobileSettingsService _settings;
    private readonly IMobileApiClient _apiClient;
    private readonly IOnDeviceCaptionService _onDeviceCaptions;

    [ObservableProperty]
    private string _serverUrl;

    [ObservableProperty]
    private string _selectedStyle;

    [ObservableProperty]
    private bool _autoSaveToGallery;

    [ObservableProperty]
    private string _connectionStatus = "Not tested";

    [ObservableProperty]
    private bool _isTesting;

    [ObservableProperty]
    private bool _useOnDeviceCaptions;

    [ObservableProperty]
    private bool _useHardwareAcceleration;

    [ObservableProperty]
    private string _onDeviceModelName = string.Empty;

    [ObservableProperty]
    private string _onDeviceModelStatus = string.Empty;

    [ObservableProperty]
    private string _selectedModel;

    [ObservableProperty]
    private bool _lockGalleryWithBiometrics;

    public string[] AvailableStyles { get; } =
    [
        "Cyberpunk",
        "Anime / Studio Ghibli",
        "Oil Painting Masterpiece",
        "Cinematic 3D Render",
        "Watercolor Sketch",
        "Pixel Art 16-bit",
        "Retro Vintage 70s"
    ];

    public SettingsViewModel(
        IMobileSettingsService settings,
        IMobileApiClient apiClient,
        IOnDeviceCaptionService onDeviceCaptions)
    {
        _settings = settings;
        _apiClient = apiClient;
        _onDeviceCaptions = onDeviceCaptions;
        _serverUrl = _settings.ServerUrl;
        _selectedStyle = _settings.SelectedStyle;
        _autoSaveToGallery = _settings.AutoSaveToGallery;
        _useOnDeviceCaptions = _settings.UseOnDeviceCaptions;
        _useHardwareAcceleration = _settings.UseHardwareAcceleration;
        _onDeviceModelName = _onDeviceCaptions.Model.DisplayName;
        _selectedModel = _onDeviceCaptions.Model.DisplayName;
        _lockGalleryWithBiometrics = _settings.LockGalleryWithBiometrics;
        RefreshOnDeviceModel();
    }

    /// <summary>Display names of every catalogued on-device model, biggest last.</summary>
    public string[] AvailableModels { get; } =
        OnDeviceModelCatalog.All.Select(m => m.DisplayName).ToArray();

    /// <summary>
    /// Re-checks the filesystem for the model. Bound to a button rather than run once at startup so
    /// an adb push lands without needing an app restart to be noticed.
    /// </summary>
    [RelayCommand]
    public void RefreshOnDeviceModel()
    {
        var status = _onDeviceCaptions.Probe();
        var hwNote = _settings.UseHardwareAcceleration ? "NNAPI (NPU/GPU)" : "CPU";
        OnDeviceModelStatus = status.IsAvailable
            ? $"✅ {status.Detail} (Mode: {hwNote})"
            : $"⚠️ {status.Detail}";
    }

    [RelayCommand]
    public async Task TestConnectionAsync()
    {
        IsTesting = true;
        ConnectionStatus = "Connecting…";
        _settings.ServerUrl = ServerUrl;

        try
        {
            var isAlive = await _apiClient.PingAsync();
            if (isAlive)
            {
                ConnectionStatus = "✅ Connected to backend successfully!";
            }
            else
            {
                ConnectionStatus = "❌ Server unreachable. Verify your IP/port and ensure backend is running.";
            }
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"❌ Error: {ex.Message}";
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    public void SaveSettings()
    {
        _settings.ServerUrl = ServerUrl;
        _settings.SelectedStyle = SelectedStyle;
        _settings.AutoSaveToGallery = AutoSaveToGallery;
        _settings.UseOnDeviceCaptions = UseOnDeviceCaptions;
        _settings.UseHardwareAcceleration = UseHardwareAcceleration;
        _settings.LockGalleryWithBiometrics = LockGalleryWithBiometrics;

        // Map the picked display name back to the catalog id the caption service resolves by.
        var match = OnDeviceModelCatalog.All.FirstOrDefault(m => m.DisplayName == SelectedModel);
        if (match is not null)
            _settings.SelectedModelId = match.Id;

        RefreshOnDeviceModel();
    }
}
