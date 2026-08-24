using PoRedoImage.Mobile.Services;
using PoRedoImage.Mobile.ViewModels;
using PoRedoImage.Mobile.Views;

namespace PoRedoImage.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>();

        // ── Core Mobile Services ──────────────────────────────
        builder.Services.AddSingleton<IImageOptimizationService, ImageOptimizationService>();
        builder.Services.AddSingleton<ICameraService, MauiCameraService>();
        builder.Services.AddSingleton<IMobileSettingsService, MobileSettingsService>();
        builder.Services.AddSingleton<IMobileApiClient, MobileApiClient>();
        builder.Services.AddSingleton<IShareService, MauiShareService>();

        // ── ViewModels ────────────────────────────────────────
        builder.Services.AddTransient<MainViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();

        // ── Views / Pages ─────────────────────────────────────
        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<SettingsPage>();

        return builder.Build();
    }
}

