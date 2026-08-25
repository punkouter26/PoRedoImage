using Microsoft.Extensions.DependencyInjection;
using PoRedoImage.Mobile.Services;

namespace PoRedoImage.Mobile;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell(_services));

        // Once a caption has been generated the model holds several hundred megabytes of native
        // memory. Android ranks backgrounded apps for killing by total footprint, so keeping it
        // resident while the user is elsewhere is what gets the process reaped — and being reaped
        // costs them the photo they captured. Reloading on the next meme costs a few seconds.
        window.Stopped += (_, _) => _services.GetService<IOnDeviceCaptionService>()?.Unload();

        return window;
    }
}
