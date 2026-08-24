using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using PoRedoImage.Mobile.Services;
using PoRedoImage.Shared.DTOs;

namespace PoRedoImage.Mobile.ViewModels;

public enum ResultMode
{
    None,
    Meme,
    Regenerate,
    RapRoast,
    Describe
}

public partial class MainViewModel : ObservableObject
{
    private readonly ICameraService _cameraService;
    private readonly IMobileApiClient _apiClient;
    private readonly IShareService _shareService;
    private readonly IMobileSettingsService _settings;

    [ObservableProperty]
    private ImageCaptureResult? _capturedImage;

    [ObservableProperty]
    private Microsoft.Maui.Controls.ImageSource? _photoImageSource;

    [ObservableProperty]
    private string _photoSummary = string.Empty;

    [ObservableProperty]
    private bool _hasPhoto;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private string _processingStage = "Ready";

    [ObservableProperty]
    private double _processingProgress;

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private ResultMode _currentResultMode = ResultMode.None;

    [ObservableProperty]
    private string _resultTitle = string.Empty;

    [ObservableProperty]
    private string _resultSubtitle = string.Empty;

    [ObservableProperty]
    private string _resultText = string.Empty;

    [ObservableProperty]
    private Microsoft.Maui.Controls.ImageSource? _resultImageSource;

    [ObservableProperty]
    private byte[]? _resultImageBytes;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _selectedStyle = "Cyberpunk";

    public MainViewModel(
        ICameraService cameraService,
        IMobileApiClient apiClient,
        IShareService shareService,
        IMobileSettingsService settings)
    {
        _cameraService = cameraService;
        _apiClient = apiClient;
        _shareService = shareService;
        _settings = settings;
        _selectedStyle = _settings.SelectedStyle;
    }

    [RelayCommand]
    public async Task TakePhotoAsync()
    {
        ClearError();
        ProcessingStage = "Opening camera…";
        try
        {
            var result = await _cameraService.CapturePhotoAsync();
            if (result != null)
            {
                SetCapturedPhoto(result);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Camera error: {ex.Message}";
            HasError = true;
        }
        finally
        {
            if (!HasPhoto)
            {
                ProcessingStage = "Ready";
            }
        }
    }

    [RelayCommand]
    public async Task PickPhotoAsync()
    {
        ClearError();
        ProcessingStage = "Selecting photo…";
        try
        {
            var result = await _cameraService.PickPhotoAsync();
            if (result != null)
            {
                SetCapturedPhoto(result);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Gallery error: {ex.Message}";
            HasError = true;
        }
        finally
        {
            if (!HasPhoto)
            {
                ProcessingStage = "Ready";
            }
        }
    }

    private void SetCapturedPhoto(ImageCaptureResult photo)
    {
        CapturedImage = photo;
        PhotoImageSource = ImageSource.FromStream(() => new MemoryStream(photo.Bytes));
        PhotoSummary = photo.FormattedSummary;
        HasPhoto = true;
        HasResult = false;
        ResultImageSource = null;
        ResultImageBytes = null;
        ResultText = string.Empty;
        CurrentResultMode = ResultMode.None;
    }

    [RelayCommand]
    public async Task ProcessMemeAsync()
    {
        if (CapturedImage == null) return;
        await ExecuteProcessingAsync("Meme Magic", async () =>
        {
            ProcessingStage = "Analyzing photo scene…";
            ProcessingProgress = 0.25;

            var response = await _apiClient.ProcessMemeAsync(CapturedImage);

            ProcessingStage = "Composing meme layout…";
            ProcessingProgress = 0.75;

            if (!string.IsNullOrEmpty(response.MemeImageData))
            {
                var base64 = ExtractBase64(response.MemeImageData);
                ResultImageBytes = Convert.FromBase64String(base64);
                ResultImageSource = Microsoft.Maui.Controls.ImageSource.FromStream(() => new MemoryStream(ResultImageBytes));
            }
            else
            {
                ResultImageBytes = CapturedImage.Bytes;
                ResultImageSource = PhotoImageSource;
            }

            ResultTitle = "🎭 AI Meme Created";
            ResultSubtitle = response.Description;
            ResultText = response.MemeCaption ?? response.Description;
            CurrentResultMode = ResultMode.Meme;
            HasResult = true;
        });
    }

    [RelayCommand]
    public async Task ProcessRegenerateAsync()
    {
        if (CapturedImage == null) return;
        await ExecuteProcessingAsync("AI Art Transformation", async () =>
        {
            ProcessingStage = "Applying style: " + SelectedStyle + "…";
            ProcessingProgress = 0.3;

            var response = await _apiClient.ProcessRegenerationAsync(
                CapturedImage, $"Style: {SelectedStyle}, high detail, 4k masterwork");

            ProcessingStage = "Finalizing artwork…";
            ProcessingProgress = 0.85;

            if (!string.IsNullOrEmpty(response.RegeneratedImageData))
            {
                var base64 = ExtractBase64(response.RegeneratedImageData);
                ResultImageBytes = Convert.FromBase64String(base64);
                ResultImageSource = Microsoft.Maui.Controls.ImageSource.FromStream(() => new MemoryStream(ResultImageBytes));
            }

            ResultTitle = $"✨ Reimagined ({SelectedStyle})";
            ResultSubtitle = response.Description;
            ResultText = response.Description;
            CurrentResultMode = ResultMode.Regenerate;
            HasResult = true;
        });
    }

    [RelayCommand]
    public async Task ProcessRapRoastAsync()
    {
        if (CapturedImage == null) return;
        await ExecuteProcessingAsync("Rap Roast", async () =>
        {
            ProcessingStage = "Inspecting details to roast…";
            ProcessingProgress = 0.3;

            var response = await _apiClient.ProcessRapRoastAsync(
                CapturedImage, RapStyle.BoomBap, RoastIntensity.Roast);

            ProcessingStage = "Dropping 16 bars of heat…";
            ProcessingProgress = 0.8;

            ResultTitle = "🎤 Savage Rap Roast";
            ResultSubtitle = response.ImageDescription;
            ResultText = response.Lyrics;
            ResultImageSource = PhotoImageSource;
            ResultImageBytes = CapturedImage.Bytes;
            CurrentResultMode = ResultMode.RapRoast;
            HasResult = true;
        });
    }

    [RelayCommand]
    public async Task ProcessDescribeAsync()
    {
        if (CapturedImage == null) return;
        await ExecuteProcessingAsync("Scene Vision", async () =>
        {
            ProcessingStage = "Vision model analyzing scene…";
            ProcessingProgress = 0.5;

            var description = await _apiClient.DescribeImageAsync(CapturedImage);

            ResultTitle = "🔍 Visual Analysis";
            ResultSubtitle = "GPT-4o Vision Breakdown";
            ResultText = description;
            ResultImageSource = PhotoImageSource;
            ResultImageBytes = CapturedImage.Bytes;
            CurrentResultMode = ResultMode.Describe;
            HasResult = true;
        });
    }

    [RelayCommand]
    public async Task ShareResultAsync()
    {
        if (ResultImageBytes != null && CurrentResultMode != ResultMode.RapRoast && CurrentResultMode != ResultMode.Describe)
        {
            var fileName = $"poredo_{DateTime.UtcNow:yyyyMMdd_HHmmss}.jpg";
            await _shareService.ShareImageAsync(ResultImageBytes, fileName, ResultTitle);
        }
        else if (!string.IsNullOrEmpty(ResultText))
        {
            await _shareService.ShareTextAsync(ResultText, ResultTitle);
        }
    }

    [RelayCommand]
    public async Task SaveResultAsync()
    {
        if (ResultImageBytes != null)
        {
            var fileName = $"poredo_{DateTime.UtcNow:yyyyMMdd_HHmmss}.jpg";
            var path = await _shareService.SaveToDeviceAsync(ResultImageBytes, fileName);
            if (path != null)
            {
                ProcessingStage = "Saved to device!";
            }
        }
    }

    [RelayCommand]
    public void Reset()
    {
        CapturedImage = null;
        PhotoImageSource = null;
        PhotoSummary = string.Empty;
        HasPhoto = false;
        HasResult = false;
        ResultImageSource = null;
        ResultImageBytes = null;
        ResultText = string.Empty;
        CurrentResultMode = ResultMode.None;
        ClearError();
    }

    private async Task ExecuteProcessingAsync(string operationName, Func<Task> action)
    {
        ClearError();
        IsProcessing = true;
        ProcessingProgress = 0.1;
        ProcessingStage = $"Starting {operationName}…";

        try
        {
            await action();
            ProcessingProgress = 1.0;
            ProcessingStage = "Done!";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            HasError = true;
            ProcessingStage = "Error occurred";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private void ClearError()
    {
        ErrorMessage = null;
        HasError = false;
    }

    private static string ExtractBase64(string previewUrl)
    {
        var idx = previewUrl.IndexOf(";base64,", StringComparison.OrdinalIgnoreCase);
        return idx >= 0 ? previewUrl[(idx + 8)..] : previewUrl;
    }
}

