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
    private readonly IOnDeviceCaptionService _onDeviceCaptions;

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

    /// <summary>
    /// Says where the caption came from. Shown under every meme, because "the AI wrote this on your
    /// phone" and "the AI wrote this in Azure" are different products and the user should not have
    /// to guess which one they got.
    /// </summary>
    [ObservableProperty]
    private string _captionSourceNote = string.Empty;

    public MainViewModel(
        ICameraService cameraService,
        IMobileApiClient apiClient,
        IShareService shareService,
        IMobileSettingsService settings,
        IOnDeviceCaptionService onDeviceCaptions)
    {
        _cameraService = cameraService;
        _apiClient = apiClient;
        _shareService = shareService;
        _settings = settings;
        _onDeviceCaptions = onDeviceCaptions;
        _selectedStyle = _settings.SelectedStyle;
    }

    [RelayCommand]
    public async Task TakePhotoAsync()
    {
        await CaptureAsync(
            stage => _cameraService.CapturePhotoAsync(stage),
            "Opening camera…",
            "Camera error");
    }

    [RelayCommand]
    public async Task PickPhotoAsync()
    {
        await CaptureAsync(
            stage => _cameraService.PickPhotoAsync(stage),
            "Selecting photo…",
            "Gallery error");
    }

    /// <summary>
    /// Shared camera/gallery flow. The progress bar only starts once the picker hands the
    /// photo back, so it tracks the on-device optimization the user actually waits through
    /// rather than the time they spent composing the shot.
    /// </summary>
    private async Task CaptureAsync(
        Func<IProgress<string>, Task<ImageCaptureResult?>> capture,
        string openingStage,
        string errorPrefix)
    {
        ClearError();
        ProcessingStage = openingStage;
        ProcessingProgress = 0;

        var creep = new CancellationTokenSource();
        var creepStarted = false;
        try
        {
            var stage = new Progress<string>(text =>
            {
                ProcessingStage = text;
                IsProcessing = true;
                if (!creepStarted)
                {
                    creepStarted = true;
                    _ = CreepProgressAsync(creep.Token);
                }
            });

            var result = await capture(stage);
            if (result != null)
            {
                ProcessingProgress = 1.0;
                SetCapturedPhoto(result);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"{errorPrefix}: {ex.Message}";
            HasError = true;
        }
        finally
        {
            creep.Cancel();
            IsProcessing = false;
            ProcessingProgress = 0;
            if (!HasPhoto)
            {
                ProcessingStage = "Ready";
            }
        }
    }

    /// <summary>
    /// Eases the progress bar toward — but never to — completion while the optimizer runs.
    /// ImageSharp reports no real progress, so the curve is time-based against the ~7s a
    /// full-resolution phone photo takes; the caller snaps it to 1.0 on success.
    /// </summary>
    private async Task CreepProgressAsync(CancellationToken ct)
    {
        const double ceiling = 0.92;
        const double expectedSeconds = 7.0;
        var elapsed = 0.0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(100, ct);
                elapsed += 0.1;
                ProcessingProgress = ceiling * (1 - Math.Exp(-elapsed / (expectedSeconds / 2.5)));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
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
        CaptionSourceNote = string.Empty;
        CurrentResultMode = ResultMode.None;
    }

    [RelayCommand]
    public async Task ProcessMemeAsync()
    {
        if (CapturedImage == null) return;
        await ExecuteProcessingAsync("Meme Magic", async () =>
        {
            if (_settings.UseOnDeviceCaptions)
            {
                await ProcessMemeOnDeviceAsync(CapturedImage);
            }
            else
            {
                await ProcessMemeOnServerAsync(CapturedImage);
            }

            CurrentResultMode = ResultMode.Meme;
            HasResult = true;
        });
    }

    private async Task ProcessMemeOnServerAsync(ImageCaptureResult photo)
    {
        ProcessingStage = "Analyzing photo scene…";
        ProcessingProgress = 0.25;

        var response = await _apiClient.ProcessMemeAsync(photo);

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
            ResultImageBytes = photo.Bytes;
            ResultImageSource = PhotoImageSource;
        }

        ResultTitle = "🎭 AI Meme Created";
        ResultSubtitle = response.Description;
        ResultText = response.MemeCaption ?? response.Description;
        CaptionSourceNote = string.Empty;
    }

    /// <summary>
    /// Splits the meme between the two machines: the server still describes the photo, because
    /// Qwen2.5 is text-only and the phone has no vision model, and the caption itself is written
    /// locally. The result is the untouched photo plus caption text rather than a composited image —
    /// the layout step lives on the server with the fonts.
    /// </summary>
    /// <remarks>
    /// There is deliberately no automatic fall back to <see cref="ProcessMemeOnServerAsync"/> when
    /// the local model is missing or fails. Choosing the on-device model is a choice not to send the
    /// work to a metered service, and quietly overriding it would bill the user for a call they
    /// opted out of — the same rule the web client's LocalInferenceException follows.
    /// </remarks>
    private async Task ProcessMemeOnDeviceAsync(ImageCaptureResult photo)
    {
        ProcessingStage = "Analyzing photo scene…";
        ProcessingProgress = 0.2;

        var description = await _apiClient.DescribeImageAsync(photo);

        ProcessingProgress = 0.45;
        var stage = new Progress<string>(text => ProcessingStage = text);
        var caption = await _onDeviceCaptions.GenerateMemeCaptionAsync(description, stage);

        ProcessingProgress = 0.9;

        ResultImageBytes = photo.Bytes;
        ResultImageSource = PhotoImageSource;
        ResultTitle = "🎭 On-Device Meme";
        ResultSubtitle = description;
        ResultText = caption;
        CaptionSourceNote =
            $"Caption written on this phone by {_onDeviceCaptions.Model.DisplayName}. " +
            "The scene description still came from the server's vision model.";
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
        CaptionSourceNote = string.Empty;
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

