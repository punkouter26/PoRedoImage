using Microsoft.Extensions.DependencyInjection;
using PoRedoImage.Mobile.ViewModels;

namespace PoRedoImage.Mobile.Views;

public partial class SettingsPage : ContentPage
{
    public SettingsPage() : this(IPlatformApplication.Current?.Services.GetService<SettingsViewModel>() ?? CreateFallbackViewModel())
    {
    }

    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    private static SettingsViewModel CreateFallbackViewModel()
    {
        var sp = IPlatformApplication.Current?.Services;
        if (sp != null)
        {
            var vm = ActivatorUtilities.CreateInstance<SettingsViewModel>(sp);
            return vm;
        }
        throw new InvalidOperationException("ServiceProvider not available");
    }
}
