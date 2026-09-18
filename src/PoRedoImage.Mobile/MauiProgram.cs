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

        // Native entry points a browser page cannot have: share-target photos in, render
        // notifications out. Android gets the real thing; elsewhere a documented no-op.
#if ANDROID
        builder.Services.AddSingleton<IRenderMonitorService, AndroidRenderMonitor>();
        builder.Services.AddSingleton<ISharedImageInbox, Platforms.Android.AndroidSharedImageInbox>();
        builder.Services.AddSingleton<IBiometricGuard, Platforms.Android.BiometricGuard>();
#else
        builder.Services.AddSingleton<IRenderMonitorService, NullRenderMonitorService>();
        builder.Services.AddSingleton<ISharedImageInbox, NullSharedImageInbox>();
        builder.Services.AddSingleton<IBiometricGuard, NullBiometricGuard>();
#endif

        // ── On-Device AI ──────────────────────────────────────
        // Singleton because the caption service caches several hundred megabytes of loaded
        // weights; a transient would re-map them on every meme.
        builder.Services.AddSingleton<IOnDeviceModelStore, OnDeviceModelStore>();
        builder.Services.AddSingleton<IOnDeviceCaptionService, QwenCaptionService>();

        // ── ViewModels ────────────────────────────────────────
        builder.Services.AddTransient<MainViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<GalleryViewModel>();

        // ── Views / Pages ─────────────────────────────────────
        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<GalleryPage>();
        // CameraX pro-capture (#9) was deferred: the AndroidX binding API surface on CameraX 1.4
        // did not line up with the docs we developed against. The Pro Shot button on MainPage
        // falls back to MediaPicker, which still meets the user goal even though it does not
        // expose torch / tap-to-focus. See SPEC.md §15.

        return builder.Build();
    }
}

