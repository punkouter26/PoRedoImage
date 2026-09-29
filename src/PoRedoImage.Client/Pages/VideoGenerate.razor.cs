using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using PoRedoImage.Client.Services;
using PoRedoImage.Client.Shared;
using PoRedoImage.Shared.DTOs;
using PoRedoImage.Shared.Json;
using Radzen;

namespace PoRedoImage.Client.Pages;

/// <summary>
/// Code-behind for <c>VideoGenerate.razor</c> — the image-to-video feature.
/// </summary>
/// <remarks>
/// The server starts a Veo job and hands back the provider's operation handle. Polling it is
/// <see cref="JobTrayService"/>'s job, not this page's: a Veo render takes 1–5 minutes, and when
/// the loop lived here, leaving the page cancelled it and threw the render away. The page now
/// only mirrors the tray's job, and re-attaches to it when the user comes back.
/// </remarks>
public partial class VideoGenerate
{
    [Inject] private JobTrayService Jobs { get; set; } = default!;

    private string _prompt = string.Empty;
    private string? _videoUrl;
    private TrayJob? _job;

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        Jobs.OnChange += OnJobsChanged;
        _job = Jobs.LatestVideo;
        SyncFromJob();
    }

    private void OnJobsChanged() => InvokeAsync(() =>
    {
        // A reload lands here before the tray has restored persisted jobs, so adopt one that
        // appears while this page is idle.
        if (_job is null && !isProcessing && !isComplete) _job = Jobs.LatestVideo;
        SyncFromJob();
        StateHasChanged();
    });

    private void SyncFromJob()
    {
        if (_job is null) return;
        switch (_job.Status)
        {
            case BoardStatus.Working:
                if (!isProcessing) isProcessing = true;
                progressMessage = _job.Detail ?? "Veo is rendering…";
                break;
            case BoardStatus.Done when !isComplete:
                _videoUrl = _job.ResultUrl;
                isComplete = true;
                Jobs.MarkSeen(_job);
                break;
            case BoardStatus.Failed:
                errorMessage = _job.Detail;
                isProcessing = false;
                break;
        }
    }

    private async Task CreateVideo()
    {
        if (imagePreviewUrl is null || string.IsNullOrWhiteSpace(_prompt)) return;

        errorMessage = null;
        _videoUrl = null;
        isProcessing = true;
        progressMessage = "Sending your photo to Veo…";
        StateHasChanged();

        try
        {
            var request = new VideoGenerateRequest(
                ImageData: ExtractBase64(imagePreviewUrl),
                ContentType: SessionService.ContentType ?? selectedFile?.ContentType ?? "image/jpeg",
                Prompt: _prompt.Trim());

            using var startResponse = await Http.PostAsJsonAsync(
                "/api/video/generate", request, SharedJsonOptions.Default);

            if (!startResponse.IsSuccessStatusCode)
            {
                if (await HandleAnalyzeErrorAsync(startResponse)) return;
                isProcessing = false;
                return;
            }

            var start = await startResponse.Content.ReadFromJsonAsync<VideoGenerateStartResponse>(
                SharedJsonOptions.Default);

            if (start is null || string.IsNullOrWhiteSpace(start.OperationName))
            {
                errorMessage = "The video service did not accept the job.";
                isProcessing = false;
                return;
            }

            Cost.RecordVideo(1);
            _job = Jobs.TrackVideo(start.OperationName);
            SyncFromJob();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Video generation failed to start");
            errorMessage = "Video generation failed. Please try again.";
            isProcessing = false;
        }
    }

    private void StartOver()
    {
        // "Try another" is done with this clip, so it leaves the tray too.
        if (_job is { Status: not BoardStatus.Working }) Jobs.Dismiss(_job);
        _job = null;
        _videoUrl = null;
        _prompt = string.Empty;
        errorMessage = null;
        isComplete = false;
        isProcessing = false;
    }

    /// <summary>
    /// Triggers a browser download of the rendered clip. The video bytes arrived base64-encoded in
    /// the polling response; reconstitute them on the client and hand the resulting blob URL to the
    /// browser so the user gets a real .mp4 file rather than a navigation to a data: URI.
    /// </summary>
    private async Task DownloadVideoAsync()
    {
        if (string.IsNullOrEmpty(_videoUrl)) return;
        // downloadImage fetches any URL (data: included) into a blob and returns false on failure.
        if (!await Js.InvokeAsync<bool>("downloadImage", _videoUrl, "poredoimage.mp4"))
            NotificationService.Notify(NotificationSeverity.Error, "Download failed",
                "The clip couldn't be saved. Try again, or right-click the video and choose Save.");
    }

    public override void Dispose()
    {
        base.Dispose();
        Jobs.OnChange -= OnJobsChanged;
    }
}
