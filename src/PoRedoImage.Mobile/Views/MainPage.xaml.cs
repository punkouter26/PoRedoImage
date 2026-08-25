using Microsoft.Extensions.DependencyInjection;
using PoRedoImage.Mobile.ViewModels;

namespace PoRedoImage.Mobile.Views;

public partial class MainPage : ContentPage
{
    public MainPage() : this(IPlatformApplication.Current?.Services.GetService<MainViewModel>() ?? CreateFallbackViewModel())
    {
    }

    public MainPage(MainViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    private static MainViewModel CreateFallbackViewModel()
    {
        var sp = IPlatformApplication.Current?.Services;
        if (sp != null)
        {
            var vm = ActivatorUtilities.CreateInstance<MainViewModel>(sp);
            return vm;
        }
        throw new InvalidOperationException("ServiceProvider not available");
    }
}
