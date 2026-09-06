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
/// Code-behind for <c>RapRoast.razor</c>. The markup file keeps its directives and template;
/// all logic lives here so neither half has to be read through the other.
/// </summary>
public partial class RapRoast : FeaturePageBase
{
    private RapRoastResponse? _result;
    private RapStyle _style = RapStyle.BoomBap;
    private RoastIntensity _intensity = RoastIntensity.Roast;

    // Snapshotted when the roast is submitted, not read live: the session image can change while a
    // result is on screen, which would leave the bars describing a photo that is no longer shown.
    private string? _roastedImageUrl;

    // ── Karaoke + export state ───────────────────────────────────────────────
    private ElementReference _audioRef;
    private ElementReference _lyricsRef;
    private IReadOnlyList<RoastLine> _lines = [];

    /// <summary>True once the JS driver confirmed the track carries a usable duration.</summary>
    private bool _karaokeLive;
    private bool _canRecord;
    private bool _exporting;

    /// <summary>-1 when idle; 0–100 while a video is recording.</summary>
    private int _exportProgress = -1;
    private double _syncOffset;
    private string _videoMode = "memeRoast";

    /// <summary>Guards against re-attaching on every render — one attach per result.</summary>
    private RapRoastResponse? _attachedTo;
    private DotNetObjectReference<RapRoast>? _selfRef;

    private static readonly (RapStyle Value, string Label, string Hint)[] StyleOptions =
    [
        (RapStyle.BoomBap, "Boom-bap", "90s, dusty drums"),
        (RapStyle.Trap, "Trap", "808s, hi-hat rolls"),
        (RapStyle.OldSchool, "Old-school", "Funk break, horns"),
    ];

    private static readonly (RoastIntensity Value, string Label, string Hint)[] IntensityOptions =
    [
        (RoastIntensity.Gentle, "Gentle", "Warm teasing, no real burns"),
        (RoastIntensity.Roast, "Roast", "Real punchlines, good-natured"),
        (RoastIntensity.Scorched, "Scorched", "Merciless — still only about choices"),
    ];

    protected override void OnGalleryImageSelected() => _result = null;

    /// <summary>Studio "Surprise me" hand-off — the default beat style is enough to run.</summary>
    protected override Task? AutoStartAsync() => RoastAsync();

    private async Task RoastAsync()
    {
        if (!canProcessImage) return;

        isProcessing = true;
        isComplete = false;
        errorMessage = null;
        _result = null;
        progressMessage = "Looking at your photo…";
        StateHasChanged();

        try
        {
            var previewUrl = imagePreviewUrl ?? SessionService.PreviewUrl;
            if (string.IsNullOrEmpty(previewUrl))
            {
                errorMessage = "No image loaded.";
                return;
            }

            progressMessage = "Writing your bars…";
            StateHasChanged();

            _roastedImageUrl = previewUrl;

            var request = new RapRoastRequest
            {
                ImageData = ExtractBase64(previewUrl),
                ContentType = SessionService.ContentType ?? "image/png",
                Style = _style,
                Intensity = _intensity,
            };

            var response = await Http.PostAsJsonAsync("/api/rap-roast", request, SharedJsonOptions.Default);

            if (!response.IsSuccessStatusCode)
            {
                // 401 = BFF session cookie missing/expired. Send to /login instead of the
                // generic "roast pipeline failed" message. See ImageRegeneration.razor for
                // the matching fix.
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    errorMessage = "Your session has expired. Please sign in again.";
                    NotificationService.Notify(
                        NotificationSeverity.Warning,
                        "Session expired",
                        "Please sign in again to continue.",
                        duration: 4000);
                    NavigationManager.NavigateTo("/login", forceLoad: true);
                    return;
                }

                errorMessage = response.StatusCode switch
                {
                    System.Net.HttpStatusCode.TooManyRequests =>
                        "You're roasting faster than the beat. Give it a minute and try again.",
                    System.Net.HttpStatusCode.BadRequest =>
                        "That image couldn't be read. Try a JPG or PNG.",
                    _ => "The roast pipeline failed. Please try again.",
                };
                Logger.LogWarning("Rap roast failed: {Status}", response.StatusCode);
                return;
            }

            progressMessage = "Cutting the track…";
            StateHasChanged();

            _result = await response.Content.ReadFromJsonAsync<RapRoastResponse>(SharedJsonOptions.Default);
            isComplete = _result is not null;
            if (isComplete)
            {
                Cost.RecordVision(1);
                Cost.RecordTextReasoning(1);
                if (!string.IsNullOrEmpty(_result?.AudioData) || _result?.AudioRefused == false)
                    Cost.RecordMusic(1);
            }
            _lines = RoastScript.Parse(_result?.Lyrics);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Rap roast request failed");
            errorMessage = "Something went wrong making your track. Please try again.";
        }
        finally
        {
            isProcessing = false;
            StateHasChanged();
        }
    }

    private void Reset()
    {
        _ = DetachKaraokeAsync();
        _result = null;
        _roastedImageUrl = null;
        _lines = [];
        isComplete = false;
        errorMessage = null;
        StateHasChanged();
    }

    // ── Karaoke ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Binds the JS driver once per result, after the audio element and lyric list are in the DOM.
    /// </summary>
    /// <remarks>
    /// The base class owns the "Surprise me" auto-start in its own <c>OnAfterRenderAsync</c>, so this
    /// must call through to it rather than replace it.
    /// </remarks>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);

        if (firstRender)
        {
            _canRecord = await SafeInvokeAsync<bool>("poRoast.canRecord");
            if (_canRecord) StateHasChanged();
        }

        // Nothing to sync against when the music provider refused: there is no track to follow.
        if (_result is null || ReferenceEquals(_attachedTo, _result)) return;
        if (_result.AudioRefused || _lines.Count == 0) return;

        _attachedTo = _result;
        _selfRef ??= DotNetObjectReference.Create(this);

        // False is an ordinary answer, not a failure: the mock music service returns a fraction of a
        // second of silence, and there is no honest way to spread twelve bars across it.
        var live = await SafeInvokeAsync<bool>("poRoast.attach", _audioRef, _lyricsRef, _lines, _syncOffset);
        if (live != _karaokeLive)
        {
            _karaokeLive = live;
            StateHasChanged();
        }
    }

    private async Task OnSyncChanged(ChangeEventArgs e)
    {
        if (!double.TryParse(e.Value?.ToString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var seconds))
            return;

        _syncOffset = seconds;
        await SafeInvokeVoidAsync("poRoast.setOffset", seconds);
    }

    private Task SeekToLineAsync(int index) =>
        _karaokeLive ? SafeInvokeVoidAsync("poRoast.seekToLine", _audioRef, index) : Task.CompletedTask;

    private async Task DetachKaraokeAsync()
    {
        _attachedTo = null;
        _karaokeLive = false;
        await SafeInvokeVoidAsync("poRoast.detach");
    }

    private static string FormatOffset(double seconds) =>
        seconds == 0 ? "on the beat" : $"{(seconds > 0 ? "+" : "")}{seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}s";

    // ── Export ───────────────────────────────────────────────────────────────

    /// <summary>Beat style and intensity, stamped on the exported card so a share carries its recipe.</summary>
    private string ExportMeta =>
        $"{StyleOptions.First(o => o.Value == _style).Label} · {IntensityOptions.First(o => o.Value == _intensity).Label}";

    private async Task SaveCardAsync()
    {
        if (_result is null || _exporting) return;
        _exporting = true;
        StateHasChanged();
        try
        {
            // -1 asks the JS side for whichever bar is lit right now.
            var outcome = await SafeInvokeAsync<string>(
                "poRoast.exportCard", _roastedImageUrl, _lines, -1, ExportMeta,
                _videoMode == "memeRoast" ? "meme-roast-card.png" : "rap-roast-card.png", _videoMode);

            if (outcome == "saved")
                NotificationService.Notify(NotificationSeverity.Success, "Card saved",
                    "Check your downloads.", duration: 3000);
            else
                NotificationService.Notify(NotificationSeverity.Warning, "Card not saved",
                    "The browser blocked the image export. Try again, or screenshot the player.", duration: 5000);
        }
        finally
        {
            _exporting = false;
            StateHasChanged();
        }
    }

    private async Task ExportVideoAsync()
    {
        if (_result is null || _exporting) return;
        _exporting = true;
        _exportProgress = 0;
        StateHasChanged();
        try
        {
            _selfRef ??= DotNetObjectReference.Create(this);
            var outcome = await SafeInvokeAsync<string>(
                "poRoast.exportVideo", _audioRef, _selfRef, _roastedImageUrl, _lines, ExportMeta,
                _videoMode == "memeRoast" ? "meme-video-roast.webm" : "rap-roast.webm", _videoMode);

            var (severity, title, detail) = outcome switch
            {
                "saved" => (NotificationSeverity.Success, "Video saved", "Check your downloads."),
                "unsupported" => (NotificationSeverity.Info, "Video export unavailable",
                    "This browser can't record canvas video. The card export works everywhere."),
                _ => (NotificationSeverity.Warning, "Recording failed",
                    "The track needs to play through uninterrupted to record. Try again without switching tabs."),
            };
            NotificationService.Notify(severity, title, detail, duration: 5000);
        }
        finally
        {
            _exporting = false;
            _exportProgress = -1;
            StateHasChanged();
        }
    }

    /// <summary>Called from the recorder's draw loop — the recording is real-time, so this ticks.</summary>
    [JSInvokable]
    public void OnExportProgress(int percent)
    {
        var clamped = Math.Clamp(percent, 0, 100);
        if (clamped == _exportProgress) return;
        _exportProgress = clamped;
        InvokeAsync(StateHasChanged);
    }

    // ── Interop plumbing ─────────────────────────────────────────────────────

    /// <summary>
    /// Interop that tolerates a torn-down circuit. Every call here is an enhancement — karaoke,
    /// sync, export — so a failure must leave the page working with plain lyrics rather than
    /// throwing into the render loop.
    /// </summary>
    /// <remarks>
    /// The annotation is forwarded rather than suppressed: <c>IJSRuntime.InvokeAsync&lt;T&gt;</c>
    /// deserializes into <typeparamref name="T"/> by reflection, so the trim analyzer needs the same
    /// guarantee from every caller. Both call sites here close it over <c>bool</c> and <c>string</c>.
    /// </remarks>
    private async Task<T?> SafeInvokeAsync<
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
            System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors
            | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields
            | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)] T>(
        string identifier, params object?[] args)
    {
        try
        {
            return await JS.InvokeAsync<T>(identifier, args);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or ObjectDisposedException or TaskCanceledException)
        {
            Logger.LogDebug(ex, "Roast stage interop {Identifier} unavailable", identifier);
            return default;
        }
    }

    private async Task SafeInvokeVoidAsync(string identifier, params object?[] args)
    {
        try
        {
            await JS.InvokeVoidAsync(identifier, args);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or ObjectDisposedException or TaskCanceledException)
        {
            Logger.LogDebug(ex, "Roast stage interop {Identifier} unavailable", identifier);
        }
    }

    public void Dispose()
    {
        _ = SafeInvokeVoidAsync("poRoast.detach");
        _selfRef?.Dispose();
        _selfRef = null;
    }
}
