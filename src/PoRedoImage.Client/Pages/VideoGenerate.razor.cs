using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using PoRedoImage.Client.LocalAi;
using PoRedoImage.Client.Models;
using PoRedoImage.Client.Services;
using PoRedoImage.Client.Shared;
using PoRedoImage.Domain.Entities;
using PoRedoImage.Shared.DTOs;
using PoRedoImage.Shared.Json;
using Radzen;
using Radzen.Blazor;

namespace PoRedoImage.Client.Pages;

/// <summary>
/// Code-behind for <c>VideoGenerate.razor</c> — the image-to-video feature.
/// </summary>
/// <remarks>
/// The server starts a Veo job and hands back the provider's operation handle; this page then polls
/// <c>GET /api/video/status</c> until the clip is ready. It is not a blocking call because a Veo
/// render takes one to five minutes and Azure App Service drops an idle HTTP request at ~230
/// seconds — a single long request would work locally and fail in production.
/// </remarks>
public partial class VideoGenerate : IDisposable
{
    /// <summary>How often to ask the server whether the clip is done.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(6);

    /// <summary>
    /// Give up after this long. Veo's own guidance is one to five minutes; eight is generous enough
    /// that a slow-but-healthy render still lands, while still ending rather than polling forever.
    /// </summary>
    private static readonly TimeSpan PollTimeout = TimeSpan.FromMinutes(8);

    private string _prompt = string.Empty;
    private string? _videoUrl;
    private CancellationTokenSource? _pollCts;

    /// <summary>
    /// Unlike the other feature pages, an image alone is not enough — Veo needs to be told what
    /// should happen, and an empty prompt produces a clip that ignores the user's intent.
    /// </summary>
    private bool CanCreateVideo => canProcessImage && !string.IsNullOrWhiteSpace(_prompt);

    private async Task CreateVideo()
    {
        if (imagePreviewUrl is null || string.IsNullOrWhiteSpace(_prompt)) return;

        errorMessage = null;
        _videoUrl = null;
        isProcessing = true;
        progressMessage = "Sending your photo to Veo…";
        StateHasChanged();

        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = new CancellationTokenSource(PollTimeout);
        var ct = _pollCts.Token;

        try
        {
            var request = new VideoGenerateRequest(
                ImageData: ExtractBase64(imagePreviewUrl),
                ContentType: SessionService.ContentType ?? selectedFile?.ContentType ?? "image/jpeg",
                Prompt: _prompt.Trim());

            using var startResponse = await Http.PostAsJsonAsync(
                "/api/video/generate", request, SharedJsonOptions.Default, ct);

            if (!startResponse.IsSuccessStatusCode)
            {
                if (await HandleAnalyzeErrorAsync(startResponse)) return;
                isProcessing = false;
                return;
            }

            var start = await startResponse.Content.ReadFromJsonAsync<VideoGenerateStartResponse>(
                SharedJsonOptions.Default, ct);

            if (start is null || string.IsNullOrWhiteSpace(start.OperationName))
            {
                errorMessage = "The video service did not accept the job.";
                isProcessing = false;
                return;
            }

            Cost.RecordVideo(1);
            await PollUntilDoneAsync(start.OperationName, ct);
        }
        catch (OperationCanceledException)
        {
            // Distinguish the two cancellations that reach here: the page going away (nothing to
            // say) versus the render outliving its budget (the user needs to know it stopped).
            if (_pollCts?.IsCancellationRequested == true && !_disposed)
            {
                errorMessage =
                    $"The video did not finish within {PollTimeout.TotalMinutes:0} minutes. "
                    + "Veo may still be busy — try again with a simpler prompt.";
            }
            isProcessing = false;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Video generation failed");
            errorMessage = "Video generation failed. Please try again.";
            isProcessing = false;
        }
        finally
        {
            if (!_disposed) StateHasChanged();
        }
    }

    /// <summary>
    /// Polls the server until the render finishes, fails, or the budget runs out.
    /// </summary>
    private async Task PollUntilDoneAsync(string operationName, CancellationToken ct)
    {
        var started = DateTimeOffset.UtcNow;

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(PollInterval, ct);

            var waited = DateTimeOffset.UtcNow - started;
            progressMessage = $"Veo is rendering your clip… ({waited.TotalSeconds:0}s)";
            if (!_disposed) StateHasChanged();

            var status = await Http.GetFromJsonAsync<VideoGenerateStatusResponse>(
                $"/api/video/status?op={Uri.EscapeDataString(operationName)}", SharedJsonOptions.Default, ct);

            if (status is null || !status.Done) continue;

            if (!string.IsNullOrWhiteSpace(status.ErrorMessage) || string.IsNullOrWhiteSpace(status.VideoData))
            {
                // Always name the reason. A video that just fails to appear reads as the app being
                // broken, which is the same silent-degradation trap the AI fallbacks guard against.
                errorMessage = status?.ErrorMessage ?? "The video service returned no clip.";
                isProcessing = false;
                return;
            }

            _videoUrl = $"data:{status.VideoContentType ?? "video/mp4"};base64,{status.VideoData}";
            isProcessing = false;
            isComplete = true;

            NotificationService.Notify(
                NotificationSeverity.Success, "Video ready", "Your 8-second clip is below.", duration: 4000);
            return;
        }
    }

    private void StartOver()
    {
        _pollCts?.Cancel();
        _videoUrl = null;
        _prompt = string.Empty;
        errorMessage = null;
        isComplete = false;
        isProcessing = false;
    }

    private bool _disposed;

    public void Dispose()
    {
        _disposed = true;
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;
    }
}
