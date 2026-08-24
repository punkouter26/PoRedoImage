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

    private async void OnTakePhotoClicked(object? sender, EventArgs e)
    {
        if (BindingContext is MainViewModel vm)
        {
            await vm.TakePhotoAsync();
        }
    }

    private async void OnPickPhotoClicked(object? sender, EventArgs e)
    {
        if (BindingContext is MainViewModel vm)
        {
            await vm.PickPhotoAsync();
        }
    }

    private void OnResetClicked(object? sender, EventArgs e)
    {
        if (BindingContext is MainViewModel vm)
        {
            vm.Reset();
        }
    }

    private async void OnMemeClicked(object? sender, EventArgs e)
    {
        if (BindingContext is MainViewModel vm)
        {
            await vm.ProcessMemeAsync();
        }
    }

    private async void OnReimagineClicked(object? sender, EventArgs e)
    {
        if (BindingContext is MainViewModel vm)
        {
            await vm.ProcessRegenerateAsync();
        }
    }

    private async void OnRoastClicked(object? sender, EventArgs e)
    {
        if (BindingContext is MainViewModel vm)
        {
            await vm.ProcessRapRoastAsync();
        }
    }

    private async void OnDescribeClicked(object? sender, EventArgs e)
    {
        if (BindingContext is MainViewModel vm)
        {
            await vm.ProcessDescribeAsync();
        }
    }

    private async void OnShareClicked(object? sender, EventArgs e)
    {
        if (BindingContext is MainViewModel vm)
        {
            await vm.ShareResultAsync();
        }
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (BindingContext is MainViewModel vm)
        {
            await vm.SaveResultAsync();
        }
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
