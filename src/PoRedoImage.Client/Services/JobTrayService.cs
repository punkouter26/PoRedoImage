using System.Globalization;
using System.Net.Http.Json;
using Microsoft.JSInterop;
using PoRedoImage.Client.Shared;
using PoRedoImage.Shared.DTOs;
using PoRedoImage.Shared.Json;
using Radzen;

namespace PoRedoImage.Client.Services;

/// <summary>One long-running job shown in the header tray.</summary>
public sealed class TrayJob
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    /// <summary>The page that owns the job — the tray's "Open" link.</summary>
    public required string Href { get; init; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public BoardStatus Status { get; internal set; } = BoardStatus.Working;
    /// <summary>Progress while working, the reason when failed, a summary when done.</summary>
    public string? Detail { get; internal set; }
    /// <summary>Video jobs only: the finished clip as a data URL.</summary>
    public string? ResultUrl { get; internal set; }
    /// <summary>False until the user has looked at a finished job; drives the tray badge.</summary>
    public bool Seen { get; internal set; }
    internal bool IsVideo { get; init; }
}

/// <summary>
/// App-wide tray of long-running jobs, so a Veo render or a bulk batch keeps going — and stays
/// visible — while the user moves around the app.
/// </summary>
/// <remarks>
/// <para>
/// Video jobs are <b>owned</b> here: the polling loop used to live on the Video page and its
/// <c>Dispose</c> cancelled it, so navigating away threw away a ~$0.40 render. Their Google
/// operation handles are persisted to localStorage, so a reload or a reopened tab picks the job
/// back up — the operation lives at Google, not in this tab.
/// </para>
/// <para>
/// Bulk jobs are only <b>observed</b>: the Bulk page already keeps streaming after navigation
/// (it is not disposable, and each slot auto-saves to the gallery), so it just reports progress.
/// </para>
/// </remarks>
public sealed class JobTrayService(
    HttpClient http,
    IJSRuntime js,
    NotificationService notifications,
    ILogger<JobTrayService> logger)
{
    private const string StorageKey = "po.videoJobs";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(6);

    /// <summary>
    /// Stop waiting after this long. Veo's guidance is 1–5 minutes; eight lets a slow-but-healthy
    /// render land while still ending rather than polling forever.
    /// </summary>
    public static readonly TimeSpan VideoTimeout = TimeSpan.FromMinutes(8);

    /// <summary>Google keeps a finished clip for about two days; older handles are dead weight.</summary>
    private static readonly TimeSpan HandleRetention = TimeSpan.FromDays(1);

    private readonly List<TrayJob> _jobs = [];
    private Task? _restoreTask;

    public event Action? OnChange;

    public IReadOnlyList<TrayJob> Jobs => _jobs;

    /// <summary>Running jobs plus finished ones the user has not looked at yet.</summary>
    public int AttentionCount => _jobs.Count(j => j.Status == BoardStatus.Working || !j.Seen);

    /// <summary>The newest video job, if any — the Video page re-attaches to it on return.</summary>
    public TrayJob? LatestVideo => _jobs.LastOrDefault(j => j.IsVideo);

    // ── Bulk (page-driven) ────────────────────────────────────────────────

    public TrayJob Start(string label, string href)
    {
        var job = new TrayJob { Id = Guid.NewGuid().ToString("N"), Label = label, Href = href };
        _jobs.Add(job);
        _ = RequestBrowserNotificationsAsync();
        OnChange?.Invoke();
        return job;
    }

    public void Update(TrayJob job, string detail)
    {
        job.Detail = detail;
        OnChange?.Invoke();
    }

    public void Complete(TrayJob job, bool success, string detail)
    {
        job.Status = success ? BoardStatus.Done : BoardStatus.Failed;
        job.Detail = detail;
        _ = NotifyIfHiddenAsync(job);
        OnChange?.Invoke();
    }

    // ── Video (tray-owned) ────────────────────────────────────────────────

    /// <summary>Takes over polling for a render started by <c>POST /api/video/generate</c>.</summary>
    public TrayJob TrackVideo(string operationName)
    {
        var job = AddVideo(operationName, DateTimeOffset.UtcNow);
        _ = RequestBrowserNotificationsAsync();
        _ = PersistAsync();
        _ = PollVideoAsync(job);
        return job;
    }

    /// <summary>Resumes video jobs persisted by an earlier page load. Safe to call repeatedly.</summary>
    public Task RestoreAsync() => _restoreTask ??= RestoreCoreAsync();

    private async Task RestoreCoreAsync()
    {
        string? raw;
        try { raw = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey); }
        catch (Exception ex) when (ex is JSException or InvalidOperationException) { return; }

        // Add them all oldest-first, then announce once: a Video page opened by this reload adopts
        // LatestVideo on the first change event, which must already be the newest job.
        var restored = ParseHandles(raw)
            .Where(h => DateTimeOffset.UtcNow - h.StartedAt <= HandleRetention && _jobs.All(j => j.Id != h.Op))
            .OrderBy(h => h.StartedAt)
            .Select(h => AddVideo(h.Op, h.StartedAt, notify: false))
            .ToList();
        OnChange?.Invoke();
        foreach (var job in restored) _ = PollVideoAsync(job);
        await PersistAsync(); // drops expired handles
    }

    private TrayJob AddVideo(string operationName, DateTimeOffset startedAt, bool notify = true)
    {
        var job = new TrayJob
        {
            Id = operationName,
            Label = "Video · 8s clip",
            Href = "/video",
            StartedAt = startedAt,
            IsVideo = true,
            Detail = "Veo is rendering…",
        };
        _jobs.Add(job);
        if (notify) OnChange?.Invoke();
        return job;
    }

    private async Task PollVideoAsync(TrayJob job)
    {
        // Poll at least once even when restored past the timeout: the render may well have
        // finished while the tab was closed, and one request recovers it.
        while (true)
        {
            try
            {
                var status = await http.GetFromJsonAsync<VideoGenerateStatusResponse>(
                    $"/api/video/status?op={Uri.EscapeDataString(job.Id)}", SharedJsonOptions.Default);

                if (status is { Done: true })
                {
                    if (string.IsNullOrWhiteSpace(status.VideoData))
                    {
                        // Always name the reason — a clip that silently never appears reads as the
                        // app being broken.
                        Finish(job, BoardStatus.Failed, status.ErrorMessage ?? "The video service returned no clip.");
                    }
                    else
                    {
                        job.ResultUrl = $"data:{status.VideoContentType ?? "video/mp4"};base64,{status.VideoData}";
                        Finish(job, BoardStatus.Done, "Your clip is ready.");
                        notifications.Notify(NotificationSeverity.Success, "Video ready",
                            "Your 8-second clip is waiting in the Video tab.", duration: 5000);
                    }
                    return;
                }
            }
            catch (Exception ex)
            {
                // The handle stays persisted, so a reload retries — a flaky poll must not lose a paid render.
                logger.LogWarning(ex, "Video status poll failed for {Operation}", job.Id);
                Finish(job, BoardStatus.Failed, "Couldn't check on the video. Reload to try again.");
                return;
            }

            var waited = DateTimeOffset.UtcNow - job.StartedAt;
            if (waited > VideoTimeout)
            {
                Finish(job, BoardStatus.Failed,
                    $"Not finished after {VideoTimeout.TotalMinutes:0} minutes. Reload later to check again.");
                return;
            }

            job.Detail = $"Veo is rendering… ({waited.TotalSeconds:0}s)";
            OnChange?.Invoke();
            await Task.Delay(PollInterval);
        }
    }

    private void Finish(TrayJob job, BoardStatus status, string detail)
    {
        job.Status = status;
        job.Detail = detail;
        _ = NotifyIfHiddenAsync(job);
        OnChange?.Invoke();
    }

    // ── Shared ────────────────────────────────────────────────────────────

    public void MarkSeen(TrayJob job)
    {
        if (job.Seen || job.Status == BoardStatus.Working) return;
        job.Seen = true;
        OnChange?.Invoke();
    }

    public void MarkAllSeen()
    {
        foreach (var job in _jobs.Where(j => j.Status != BoardStatus.Working)) job.Seen = true;
        OnChange?.Invoke();
    }

    /// <summary>Removes a finished job (and its persisted handle).</summary>
    public void Dismiss(TrayJob job)
    {
        if (job.Status == BoardStatus.Working) return;
        _jobs.Remove(job);
        if (job.IsVideo) _ = PersistAsync();
        OnChange?.Invoke();
    }

    /// <summary>Persists every video handle until the user dismisses the job.</summary>
    private async Task PersistAsync()
    {
        var raw = string.Join('\n', _jobs.Where(j => j.IsVideo).Select(j =>
            $"{j.StartedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}|{j.Id}"));
        try { await js.InvokeVoidAsync("localStorage.setItem", StorageKey, raw); }
        catch (Exception ex) when (ex is JSException or InvalidOperationException) { /* storage blocked: tray still works this session */ }
    }

    /// <summary>Format: one <c>unixSeconds|operationName</c> per line. Malformed lines are skipped.</summary>
    internal static IEnumerable<(string Op, DateTimeOffset StartedAt)> ParseHandles(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) yield break;
        foreach (var line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bar = line.IndexOf('|');
            if (bar <= 0 || bar == line.Length - 1) continue;
            if (!long.TryParse(line.AsSpan(0, bar), NumberStyles.Integer, CultureInfo.InvariantCulture, out var secs)) continue;
            yield return (line[(bar + 1)..], DateTimeOffset.FromUnixTimeSeconds(secs));
        }
    }

    private async Task RequestBrowserNotificationsAsync()
    {
        try { await js.InvokeVoidAsync("poUx.requestNotifications"); }
        catch (JSException) { /* unsupported — the in-app tray still shows the result */ }
    }

    private async Task NotifyIfHiddenAsync(TrayJob job)
    {
        var title = job.Status == BoardStatus.Done ? $"{job.Label} — done" : $"{job.Label} — failed";
        try { await js.InvokeVoidAsync("poUx.notifyIfHidden", title, job.Detail ?? string.Empty); }
        catch (JSException) { /* unsupported */ }
    }
}
