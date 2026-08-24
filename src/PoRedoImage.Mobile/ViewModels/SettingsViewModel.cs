using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoRedoImage.Mobile.Services;

namespace PoRedoImage.Mobile.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IMobileSettingsService _settings;
    private readonly IMobileApiClient _apiClient;

    [ObservableProperty]
    private string _serverUrl;

    [ObservableProperty]
    private string _guestId;

    [ObservableProperty]
    private string _selectedStyle;

    [ObservableProperty]
    private bool _autoSaveToGallery;

    [ObservableProperty]
    private string _connectionStatus = "Not tested";

    [ObservableProperty]
    private bool _isTesting;

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

    public SettingsViewModel(IMobileSettingsService settings, IMobileApiClient apiClient)
    {
        _settings = settings;
        _apiClient = apiClient;
        _serverUrl = _settings.ServerUrl;
        _guestId = _settings.GuestId;
        _selectedStyle = _settings.SelectedStyle;
        _autoSaveToGallery = _settings.AutoSaveToGallery;
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
            if (!isAlive)
            {
                ConnectionStatus = "❌ Server unreachable. Check IP & port.";
                return;
            }

            var isAuthOk = await _apiClient.EnsureAuthenticatedAsync();
            if (isAuthOk)
            {
                ConnectionStatus = "✅ Connected & Authenticated (Guest ready)";
            }
            else
            {
                ConnectionStatus = "⚠️ Server alive, but Auth/CSRF handshake failed.";
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
        _settings.GuestId = GuestId;
        _settings.SelectedStyle = SelectedStyle;
        _settings.AutoSaveToGallery = AutoSaveToGallery;
    }

    [RelayCommand]
    public void GenerateNewGuestId()
    {
        GuestId = $"GUEST{Random.Shared.Next(10000000, 99999999)}";
        _settings.GuestId = GuestId;
    }
}

