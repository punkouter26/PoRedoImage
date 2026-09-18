using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using PoRedoImage.Domain.Entities;
using PoRedoImage.Mobile.Models;
using PoRedoImage.Mobile.Services;
using PoRedoImage.Shared.DTOs;
using System.Collections.ObjectModel;
using ImageCaptureResult = PoRedoImage.Mobile.Models.ImageCaptureResult;

namespace PoRedoImage.Mobile.ViewModels;

public enum ResultMode
{
    None,
    Meme,
    Regenerate,
    RapRoast,
    Describe,
    Bulk,
    Video
}

public partial class MainViewModel : ObservableObject
{
    private readonly ICameraService _cameraService;
    private readonly IMobileApiClient _apiClient;
    private readonly IShareService _shareService;
    private readonly IMobileSettingsService _settings;
    private readonly IOnDeviceCaptionService _onDeviceCaptions;
    private readonly IRenderMonitorService _renderMonitor;
    private readonly IImageOptimizationService _optimizer;
    private readonly ISharedImageInbox _sharedInbox;

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

    [ObservableProperty]
    private string _videoPrompt =
        "The photo comes alive: the subject moves gently, cinematic lighting shifts, ambient sound.";

    /// <summary>Slots for the Bulk board — prefilled as pending, filled in as the stream lands.</summary>
    public ObservableCollection<BulkItemViewModel> BulkItems { get; } = [];

    [ObservableProperty]
    private bool _hasBulkResults;

    [ObservableProperty]
    private string _bulkSummary = string.Empty;

    [ObservableProperty]
    private bool _isBulkResult;

    [ObservableProperty]
    private bool _isVideoResult;

    [ObservableProperty]
    private bool _videoReady;

    [ObservableProperty]
    private string _galleryStatus = string.Empty;

    [ObservableProperty]
    private bool _hasGalleryStatus;

    private byte[]? _videoClipBytes;

    private string _videoContentType = "video/mp4";

    private UserImageKind? _lastResultKind;

    private string _resultContentType = "image/jpeg";

    public string ResultContentType
    {
        get => _resultContentType;
        set => SetProperty(ref _resultContentType, value);
    }

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
        IOnDeviceCaptionService onDeviceCaptions,
        IRenderMonitorService renderMonitor,
        IImageOptimizationService optimizer,
        ISharedImageInbox sharedInbox)
    {
        _cameraService = cameraService;
        _apiClient = apiClient;
        _shareService = shareService;
        _settings = settings;
        _onDeviceCaptions = onDeviceCaptions;
        _renderMonitor = renderMonitor;
        _optimizer = optimizer;
        _sharedInbox = sharedInbox;
        _selectedStyle = _settings.SelectedStyle;
    }

    /// <summary>
    /// Drains the native entry points on page show: an image shared from another app, or the
    /// camera-first launch the tile/widget requested. Both are invisible to a browser page.
    /// </summary>
    public async Task OnAppearingAsync()
    {
        if (_sharedInbox.TryTake(out var bytes, out var fileName, out var contentType))
        {
            try
            {
                ProcessingStage = "Optimizing shared photo…";
                await ReceivePhotoBytesAsync(fileName, contentType, bytes);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Shared image failed: {ex.Message}";
                HasError = true;
            }
            return;
        }

        if (SharedImageInbox.ConsumeCameraLaunch())
        {
            // Let the page finish appearing before the camera activity takes over the screen.
            await Task.Delay(300);
            await TakePhotoAsync();
        }
    }

    /// <summary>Bytes from the CameraX pro-capture page enter the studio here.</summary>
    public void ReceiveCapturedPhoto(ImageCaptureResult result) => SetCapturedPhoto(result);

    private async Task ReceivePhotoBytesAsync(string fileName, string contentType, byte[] bytes)
    {
        await using var stream = new MemoryStream(bytes);
        var optimized = await _optimizer.OptimizeAsync(
            stream, fileName, contentType, maxDimension: 1280, quality: 85);
        if (optimized is not null)
            SetCapturedPhoto(optimized);
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
        ResetTransientResultState();
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
        _lastResultKind = UserImageKind.Meme;
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
        _lastResultKind = UserImageKind.Meme;
        ResultContentType = "image/jpeg";
        CaptionSourceNote =
            $"Caption written on this phone by {_onDeviceCaptions.Model.DisplayName} ({_onDeviceCaptions.ExecutionProvider}). " +
            "The scene description still came from the server's vision model.";
    }

    /// <summary>
    /// Real image regeneration — the same Gemini generation path the web client uses. The old
    /// flow only asked <c>/api/images/analyze</c> for an enhanced prompt and never generated a
    /// single pixel, which read to the user as the feature being broken.
    /// </summary>
    [RelayCommand]
    public async Task ProcessRegenerateAsync()
    {
        if (CapturedImage == null) return;
        await ExecuteProcessingAsync("AI Art Transformation", async () =>
        {
            ProcessingStage = $"Applying style: {SelectedStyle}…";
            ProcessingProgress = 0.35;

            var prompt =
                $"Reimagine the subject of this photo as a {SelectedStyle} artwork — same subject and " +
                "composition, completely re-rendered in the style, high detail, 4k masterwork.";

            byte[]? generated = null;
            string contentType = "image/jpeg";

            await foreach (var item in _apiClient.GenerateBatchAsync(CapturedImage, [prompt]))
            {
                if (item.Error is not null)
                    throw new InvalidOperationException($"Generation declined: {item.Error}");

                if (item.ImageData is not null)
                {
                    generated = Convert.FromBase64String(item.ImageData);
                    contentType = item.ContentType ?? contentType;
                }
            }

            if (generated is null)
                throw new InvalidOperationException("The generator returned no image.");

            ProcessingStage = "Finalizing artwork…";
            ProcessingProgress = 0.9;

            ResultImageBytes = generated;
            ResultContentType = contentType;
            ResultImageSource = Microsoft.Maui.Controls.ImageSource.FromStream(() => new MemoryStream(generated));
            _lastResultKind = UserImageKind.Regeneration;

            ResultTitle = $"✨ Reimagined ({SelectedStyle})";
            ResultSubtitle = "Regenerated with Gemini";
            ResultText = string.Empty;
            CaptionSourceNote = string.Empty;
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
                CapturedImage, RapStyle.StandUp, RoastIntensity.Roast);

            ProcessingStage = "Dropping 16 bars of heat…";
            ProcessingProgress = 0.8;

            ResultTitle = "🎤 Savage Rap Roast";
            ResultSubtitle = response.ImageDescription;
            ResultText = response.Lyrics;
            ResultImageSource = PhotoImageSource;
            ResultImageBytes = CapturedImage.Bytes;
            _lastResultKind = null;
            CurrentResultMode = ResultMode.RapRoast;
            HasResult = true;

            // Every degradation the server made is named here — a silent fallback reads as
            // "the AI ignored my photo" (same rule as the web client).
            var notes = new List<string>();
            if (response.AudioRefused)
                notes.Add("The music provider declined to perform these bars" +
                    (string.IsNullOrEmpty(response.RefusalReason) ? "." : $": {response.RefusalReason}"));
            if (response.LyricsSoftened)
                notes.Add("Lyrics are the softened second attempt.");
            if (response.ExplicitDropped)
                notes.Add("Explicit language was dropped by the safety filter.");
            if (!string.IsNullOrEmpty(response.LyricsFallbackReason))
                notes.Add(response.LyricsFallbackReason);
            CaptionSourceNote = string.Join(" ", notes);

            RoastAudioBytes = string.IsNullOrEmpty(response.AudioData)
                ? null
                : Convert.FromBase64String(ExtractBase64(response.AudioData));
            RoastAudioContentType = string.IsNullOrEmpty(response.AudioContentType)
                ? "audio/mpeg"
                : response.AudioContentType;
        });
    }

    public byte[]? RoastAudioBytes { get; private set; }

    public string RoastAudioContentType { get; private set; } = "audio/mpeg";

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

    /// <summary>
    /// Bulk ×10 — the web client's flagship: describe the subject once, substitute it into the
    /// ten default art-style prompts, and stream the batch so slots fill in live.
    /// </summary>
    [RelayCommand]
    public async Task ProcessBulkAsync()
    {
        if (CapturedImage == null) return;
        await ExecuteProcessingAsync("Bulk Art Studio", async () =>
        {
            ProcessingStage = "Describing the subject…";
            ProcessingProgress = 0.15;

            var description = await _apiClient.DescribeImageAsync(CapturedImage);
            var safeDescription = SanitizeDescription(description);

            BulkItems.Clear();
            for (var i = 0; i < BulkPrompts.All.Length; i++)
                BulkItems.Add(new BulkItemViewModel(i));
            HasBulkResults = true;
            IsBulkResult = true;
            _lastResultKind = UserImageKind.BulkVariation;

            var prompts = BulkPrompts.All
                .Select(p => p.Replace(BulkPrompts.PersonToken, safeDescription, StringComparison.Ordinal))
                .ToArray();

            ProcessingStage = "Generating 10 variations…";
            var completed = 0;

            await foreach (var item in _apiClient.GenerateBatchAsync(CapturedImage, prompts))
            {
                var slot = BulkItems[item.Index];
                if (item.Error is not null)
                {
                    slot.IsFailed = true;
                    slot.StatusText = item.Error;
                }
                else if (item.ImageData is not null)
                {
                    slot.Bytes = Convert.FromBase64String(item.ImageData);
                    slot.ContentType = item.ContentType ?? "image/jpeg";
                    slot.Image = Microsoft.Maui.Controls.ImageSource.FromStream(
                        () => new MemoryStream(slot.Bytes));
                    slot.IsFilled = true;
                    slot.StatusText = $"Style #{item.Index + 1} ready";
                }

                completed++;
                BulkSummary = $"{completed}/{BulkItems.Count} slots done";
                ProcessingProgress = Math.Min(0.95, 0.2 + 0.75 * completed / (double)BulkItems.Count);
            }

            var succeeded = BulkItems.Count(b => b.IsFilled);
            BulkSummary = $"{succeeded}/{BulkItems.Count} variations generated";
            ResultTitle = "🎨 Bulk Art Studio";
            ResultSubtitle = BulkSummary;
            ResultText = string.Empty;
            CaptionSourceNote = string.Empty;
            CurrentResultMode = ResultMode.Bulk;
            HasResult = true;
        });
    }

    /// <summary>
    /// Video — photo + prompt in, an 8-second Veo clip out. Two calls by design: the handle
    /// returns immediately and is polled, because Veo renders for 1–5 minutes and long-held
    /// requests die behind proxies.
    /// </summary>
    [RelayCommand]
    public async Task ProcessVideoAsync()
    {
        if (CapturedImage == null) return;
        var prompt = string.IsNullOrWhiteSpace(VideoPrompt) ? string.Empty : VideoPrompt.Trim();

        await ExecuteProcessingAsync("Video Render", async () =>
        {
            if (prompt.Length is < 3 or > 1200)
                throw new InvalidOperationException("Video prompt must be between 3 and 1200 characters.");

            ProcessingStage = "Submitting clip to Veo…";
            ProcessingProgress = 0.15;

            var operation = await _apiClient.StartVideoAsync(CapturedImage, prompt);
            var startedAt = DateTime.UtcNow;

            // Foreground service + notification: without it Android freezes the app the moment
            // the user locks the phone, and a 1-5 minute render dies silently in the background.
            await _renderMonitor.StartAsync("Rendering your PoRedo clip…");
            var succeeded = false;
            try
            {
                while (true)
                {
                    var status = await _apiClient.PollVideoAsync(operation);
                    if (status.Done)
                    {
                        if (!string.IsNullOrEmpty(status.ErrorMessage))
                            throw new InvalidOperationException($"Clip declined: {status.ErrorMessage}");
                        if (string.IsNullOrEmpty(status.VideoData))
                            throw new InvalidOperationException("The video service returned no clip.");

                        _videoClipBytes = Convert.FromBase64String(status.VideoData);
                        _videoContentType = string.IsNullOrEmpty(status.VideoContentType)
                            ? "video/mp4"
                            : status.VideoContentType;
                        break;
                    }

                    var elapsed = (int)(DateTime.UtcNow - startedAt).TotalSeconds;
                    ProcessingStage = $"Rendering clip… {elapsed}s elapsed (typically 1–5 min)";
                    ProcessingProgress = Math.Min(0.95, 0.2 + elapsed / 300.0 * 0.75);
                    await Task.Delay(TimeSpan.FromSeconds(8));
                }

                succeeded = true;
            }
            finally
            {
                await _renderMonitor.CompleteAsync(
                    succeeded ? "Your 8-second clip is ready 🎬" : "Clip render failed — open PoRedo for details.",
                    succeeded);
            }

            VideoReady = true;
            _lastResultKind = null; // clips are not gallery images — the server stores kinds for pictures only
            ResultTitle = "🎬 Video Ready";
            ResultSubtitle = prompt;
            ResultText = "Your 8-second clip with sound is ready. Open it to watch, or share it.";
            CaptionSourceNote = string.Empty;
            ResultImageSource = PhotoImageSource;
            ResultImageBytes = CapturedImage.Bytes;
            CurrentResultMode = ResultMode.Video;
            IsVideoResult = true;
            HasResult = true;
        });
    }

    /// <summary>
    /// Writes the finished clip to the app cache and hands it to the platform player.
    /// </summary>
    [RelayCommand]
    public async Task OpenVideoClipAsync()
    {
        if (_videoClipBytes is null) return;
        var path = Path.Combine(FileSystem.CacheDirectory, $"poredo_{DateTime.UtcNow:yyyyMMdd_HHmmss}.mp4");
        await File.WriteAllBytesAsync(path, _videoClipBytes);
        await Launcher.Default.OpenAsync(new OpenFileRequest
        {
            Title = "PoRedo clip",
            File = new ReadOnlyFile(path, _videoContentType)
        });
    }

    /// <summary>
    /// Persists the current original + result to the PoRedo gallery — the persistence the web
    /// app has always had. Requires the guest session; fails honestly when the server has no
    /// guest login (Production) rather than pretending to save.
    /// </summary>
    [RelayCommand]
    public async Task SaveToGalleryAsync()
    {
        if (CapturedImage == null) return;
        await ExecuteProcessingAsync("Gallery save", async () =>
        {
            if (!await _apiClient.EnsureAuthenticatedAsync())
                throw new InvalidOperationException(
                    "Sign-in failed: the PoRedo gallery needs a Development or Test server (guest login " +
                    "is disabled in Production). Generation features still work — only saving is affected.");

            ProcessingStage = "Saving original…";
            ProcessingProgress = 0.4;
            await _apiClient.SaveOriginalToGalleryAsync(CapturedImage);

            ProcessingStage = "Saving result…";
            ProcessingProgress = 0.7;
            var saved = 0;

            if (CurrentResultMode == ResultMode.Bulk)
            {
                foreach (var slot in BulkItems.Where(b => b.IsFilled && b.Bytes is not null))
                {
                    await _apiClient.SaveResultToGalleryAsync(slot.Bytes!, slot.ContentType!, UserImageKind.BulkVariation);
                    saved++;
                }
            }
            else if (ResultImageBytes is not null && _lastResultKind is not null)
            {
                await _apiClient.SaveResultToGalleryAsync(ResultImageBytes, ResultContentType, _lastResultKind.Value);
                saved++;
            }

            GalleryStatus = saved == 0
                ? "Original saved — this result has no image to store."
                : $"Saved original + {saved} result{(saved == 1 ? "" : "s")} to your PoRedo gallery ✓";
            HasGalleryStatus = true;
        });
    }

    /// <summary>Trims/collapses the vision description so it substitutes cleanly into prompts.</summary>
    private static string SanitizeDescription(string description)
    {
        var trimmed = description.Trim().ReplaceLineEndings(" ");
        if (trimmed.Length > 400)
            trimmed = trimmed[..400].TrimEnd() + "…";
        return trimmed.Replace("\"", "'", StringComparison.Ordinal);
    }

    private void ResetTransientResultState()
    {
        HasBulkResults = false;
        IsBulkResult = false;
        IsVideoResult = false;
        VideoReady = false;
        BulkItems.Clear();
        BulkSummary = string.Empty;
        GalleryStatus = string.Empty;
        HasGalleryStatus = false;
        _videoClipBytes = null;
        _lastResultKind = null;
        RoastAudioBytes = null;
    }

    [RelayCommand]
    public async Task ShareResultAsync()
    {
        if (CurrentResultMode == ResultMode.Video && _videoClipBytes is not null)
        {
            await _shareService.ShareFileAsync(
                _videoClipBytes, $"poredo_{DateTime.UtcNow:yyyyMMdd_HHmmss}.mp4", ResultTitle);
            return;
        }

        if (CurrentResultMode == ResultMode.RapRoast && RoastAudioBytes is not null)
        {
            await _shareService.ShareFileAsync(
                RoastAudioBytes, $"poredo_roast_{DateTime.UtcNow:yyyyMMdd_HHmmss}.mp3", ResultTitle);
            return;
        }

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
        var meta = new MediaMetadata(
            Title: ResultTitle,
            PromptOrDescription: !string.IsNullOrWhiteSpace(ResultText) ? ResultText : PhotoSummary,
            ModelOrStyle: CurrentResultMode == ResultMode.Regenerate ? SelectedStyle : CurrentResultMode.ToString(),
            CreatedAt: DateTimeOffset.UtcNow);

        if (IsVideoResult && _videoClipBytes != null && _videoClipBytes.Length > 0)
        {
            var fileName = $"poredo_{DateTime.UtcNow:yyyyMMdd_HHmmss}.mp4";
            var path = await _shareService.SaveToDeviceAsync(_videoClipBytes, fileName, _videoContentType, meta);
            if (path != null)
            {
                ProcessingStage = $"Saved video to {path}!";
            }
        }
        else if (ResultImageBytes != null && ResultImageBytes.Length > 0)
        {
            var fileName = $"poredo_{DateTime.UtcNow:yyyyMMdd_HHmmss}.jpg";
            var path = await _shareService.SaveToDeviceAsync(ResultImageBytes, fileName, ResultContentType, meta);
            if (path != null)
            {
                ProcessingStage = $"Saved image to {path}!";
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
        ResetTransientResultState();
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

            if (_settings.AutoSaveToGallery)
            {
                await AutoSaveResultToDeviceAsync();
            }
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

    private async Task AutoSaveResultToDeviceAsync()
    {
        try
        {
            var meta = new MediaMetadata(
                Title: ResultTitle,
                PromptOrDescription: !string.IsNullOrWhiteSpace(ResultText) ? ResultText : PhotoSummary,
                ModelOrStyle: CurrentResultMode == ResultMode.Regenerate ? SelectedStyle : CurrentResultMode.ToString(),
                CreatedAt: DateTimeOffset.UtcNow);

            if (IsVideoResult && _videoClipBytes != null && _videoClipBytes.Length > 0)
            {
                var fileName = $"poredo_{DateTime.UtcNow:yyyyMMdd_HHmmss}.mp4";
                var path = await _shareService.SaveToDeviceAsync(_videoClipBytes, fileName, _videoContentType, meta);
                if (path != null)
                {
                    ProcessingStage = "Done! (Saved to device gallery)";
                }
            }
            else if (ResultImageBytes != null && ResultImageBytes.Length > 0)
            {
                var fileName = $"poredo_{DateTime.UtcNow:yyyyMMdd_HHmmss}.jpg";
                var path = await _shareService.SaveToDeviceAsync(ResultImageBytes, fileName, ResultContentType, meta);
                if (path != null)
                {
                    ProcessingStage = "Done! (Saved to device gallery)";
                }
            }
        }
        catch
        {
            // Auto-save is best-effort and must not fail the generation result
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

