using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
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
/// Code-behind for <c>BulkGenerate.razor</c>. The markup file keeps its directives and template;
/// all logic lives here so neither half has to be read through the other. Upload, paste/drop,
/// gallery pick and the original's auto-save come from <see cref="FeaturePageBase"/>.
/// </summary>
public partial class BulkGenerate
{
    private string[] _prompts = DefaultPrompts.All.ToArray();
    private int _activePromptCount => _prompts.Count(p => !string.IsNullOrWhiteSpace(p));
    private List<BulkGenerateImageResult> _results = [];
    private int _completedCount;
    private bool _isGenerating;
    private bool _isSaving;
    private HashSet<int> _favorites = [];
    private BulkGallery? _bulkGallery;
    private CancellationTokenSource? _cts;
    private bool _zipping;

    /// <summary>Completed slots that actually carry an image — the ZIP's contents.</summary>
    private List<BulkGenerateImageResult> _completedResults =>
        [.. _results.Where(r => r.Status == BulkGenerateStatus.Complete && r.ImageUrl is not null)];

    private sealed record BulkSavedState(BulkGenerateImageResult[] Results);

    /// <summary>
    /// Persists the board to localStorage so it survives leaving the page or a reload. Fire and
    /// forget: losing a save costs a restore, never the batch.
    /// </summary>
    private void SaveBoardState() =>
        _ = Js.InvokeVoidAsync("bulkStateManager.save",
            System.Text.Json.JsonSerializer.Serialize(new BulkSavedState([.. _results]), SharedJsonOptions.Default)).AsTask();

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();

        // A prompt staged by a result's Remix lands in slot 1, so the run the user just
        // asked for is the first one generated. Taken once: coming back to this page later
        // must not silently re-seed a prompt they have since edited or cleared.
        var staged = SessionService.TakeStagedPrompt();
        if (staged is not null)
        {
            _prompts[0] = staged;
            NotificationService.Notify(
                NotificationSeverity.Success,
                "Prompt Loaded",
                "Your staged prompt is in slot 1. Upload or keep your photo, then Generate.",
                duration: 5000);
        }

        // Active image-gen provider + indicative per-image pricing, shared with the footer chip
        // so the page total and the session total can't disagree. Non-critical: a failure just
        // hides the pricing note.
        await Cost.EnsureLoadedAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);

        // The component is rebuilt on every visit, so a batch that finished while the user was
        // on another page (or before a reload) is restored from localStorage.
        if (firstRender && _results.Count == 0)
        {
            try
            {
                var savedJson = await Js.InvokeAsync<string?>("bulkStateManager.load");
                if (!string.IsNullOrEmpty(savedJson))
                {
                    var state = System.Text.Json.JsonSerializer.Deserialize<BulkSavedState>(savedJson, SharedJsonOptions.Default);
                    if (state?.Results?.Length > 0)
                    {
                        _results = [.. state.Results];
                        _completedCount = _results.Count(r => r.Status == BulkGenerateStatus.Complete);
                        NotificationService.Notify(NotificationSeverity.Info, "Session Restored", $"Restored {_completedCount} result(s) from previous session.", duration: 4000);
                        StateHasChanged();
                    }
                }
            }
            catch (Exception ex)
            {
                // Non-critical: a corrupt or outdated save just means nothing is restored.
                Logger.LogDebug(ex, "Bulk board restore skipped");
            }
        }
    }

    /// <summary>A new photo means the board shows results for a photo that is no longer loaded.</summary>
    protected override void OnImageChanged()
    {
        _results = [];
        _completedCount = 0;
        _favorites = [];
    }

    private async Task StartGeneration()
    {
        if (ActiveImage() is not var (imageBytes, imageContentType)) return;

        // Clear any previously saved session state before starting a new batch
        await Js.InvokeVoidAsync("bulkStateManager.clear");
        _cts = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        _isGenerating = true;
        _completedCount = 0;

        var activePrompts = _prompts
            .Select((p, i) => (prompt: p, index: i))
            .Where(x => !string.IsNullOrWhiteSpace(x.prompt))
            .ToList();

        _results = activePrompts.Select((x, slot) => new BulkGenerateImageResult
        {
            Index = slot,
            Status = BulkGenerateStatus.Pending,
            Prompt = x.prompt
        }).ToList();

        StateHasChanged();

        // The batch keeps streaming if the user navigates away (this page is not disposable), so
        // the header tray is where they watch it from elsewhere.
        var trayJob = Jobs.Start($"Bulk · {activePrompts.Count} styles", "/bulk-generate");
        string? trayFailure = null;

        try
        {
            // ── One request, streamed ────────────────────────────────────────────
            // This was a `for` loop of one-at-a-time POSTs, each re-uploading the whole source
            // image: ten sequential round-trips for a batch of ten, and roughly 53MB of base64
            // upload for a 4MB photo. The server now fans out under its own concurrency cap and
            // streams each slot back as NDJSON, so the board still fills in one card at a time —
            // it just stops taking three times longer than it needs to.
            //
            // Slots complete OUT OF ORDER under concurrency, which is why each line carries its
            // index rather than relying on arrival sequence.
            //
            // The batch is image-to-image — Gemini already has the photo — so <PERSON> points at it
            // rather than at a vision model's noun phrase. That used to cost a /describe round trip
            // before the first card could start, and when it failed the literal token went to Gemini.
            var finalPrompts = activePrompts
                .Select(p => p.prompt.Replace(DefaultPrompts.PersonToken, "the person in the reference photo", StringComparison.Ordinal))
                .ToArray();

            foreach (var slot in _results) slot.Status = BulkGenerateStatus.Processing;
            StateHasChanged();

            using var batchRequest = new HttpRequestMessage(HttpMethod.Post, "/api/bulk-generate/batch")
            {
                Content = JsonContent.Create(
                    new BulkBatchRequest(
                        Convert.ToBase64String(imageBytes),
                        imageContentType,
                        finalPrompts),
                    options: SharedJsonOptions.Default),
            };

            // Without this the browser buffers the whole response and the stream arrives as one
            // lump at the end — which would preserve the latency win but throw away the live board.
            batchRequest.SetBrowserResponseStreamingEnabled(true);

            using var batchResponse = await Http.SendAsync(
                batchRequest, HttpCompletionOption.ResponseHeadersRead, _cts.Token);

            if (!batchResponse.IsSuccessStatusCode)
                throw new InvalidOperationException($"Batch API returned {batchResponse.StatusCode}");

            await using var stream = await batchResponse.Content.ReadAsStreamAsync(_cts.Token);
            using var reader = new StreamReader(stream);

            while (await reader.ReadLineAsync(_cts.Token) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                BulkBatchItem? item;
                try
                {
                    item = System.Text.Json.JsonSerializer.Deserialize<BulkBatchItem>(line, SharedJsonOptions.Default);
                }
                catch (System.Text.Json.JsonException ex)
                {
                    // A malformed line loses one slot, not the batch.
                    Logger.LogWarning(ex, "Unparseable batch line skipped");
                    continue;
                }

                if (item is null || item.Index < 0 || item.Index >= _results.Count) continue;

                var slot = _results[item.Index];

                if (item.ImageData is null || item.ContentType is null)
                {
                    slot.Status = BulkGenerateStatus.Failed;
                    slot.ErrorMessage = item.Error ?? "Generation failed for this variation.";
                    Logger.LogError("Bulk slot {Index} failed: {Error}", item.Index, slot.ErrorMessage);
                    _ = Feedback.FailureAsync();
                }
                else
                {
                    slot.Status = BulkGenerateStatus.Complete;
                    slot.Prompt = activePrompts[item.Index].prompt;
                    slot.ImageUrl = $"data:{item.ContentType};base64,{item.ImageData}";
                    _completedCount++;
                    Cost.RecordImages();
                    if (_userId is not null)
                        _ = AutoSaveVariationAsync(item.ImageData, item.ContentType);
                }

                StateHasChanged();
                Jobs.Update(trayJob, $"{_results.Count(r => r.Status != BulkGenerateStatus.Processing)} of {_results.Count} finished");
                SaveBoardState();
            }
            if (!_cts.Token.IsCancellationRequested)
            {
                if (_completedCount > 0)
                    NotificationService.Notify(NotificationSeverity.Success, "Done!", $"Completed {_completedCount} of {activePrompts.Count} variation{(activePrompts.Count == 1 ? "" : "s")}!", duration: 5000);
                else
                    NotificationService.Notify(NotificationSeverity.Error, "All Failed", "All generations failed. Check each card for details.", duration: 7000);

                // Arrival chime when at least one variation completed, otherwise the failure cue.
                _ = _completedCount > 0 ? Feedback.SuccessAsync("Bulk Generate") : Feedback.FailureAsync();
            }
        }
        catch (OperationCanceledException)
        {
            trayFailure = "Cancelled.";
            NotificationService.Notify(NotificationSeverity.Warning, "Cancelled", "Generation was cancelled.", duration: 4000);
        }
        catch (Exception ex)
        {
            trayFailure = $"Error during generation: {ex.Message}";
            NotificationService.Notify(NotificationSeverity.Error, "Error", trayFailure, duration: 7000);
            Logger.LogError(ex, "Bulk Generate failed");
        }
        finally
        {
            // Any slot the server never reported did not land — mark it rather than leave it
            // spinning forever. In finally so Cancel and a dropped stream are covered too; those
            // spinning slots were also persisted, so a later visit restored them still spinning.
            var unreported = _results.Where(r => r.Status == BulkGenerateStatus.Processing).ToList();
            foreach (var slot in unreported)
            {
                slot.Status = BulkGenerateStatus.Failed;
                slot.ErrorMessage = trayFailure ?? "No result was returned for this variation.";
            }
            if (unreported.Count > 0) SaveBoardState();

            Jobs.Complete(trayJob,
                success: trayFailure is null && _completedCount > 0,
                trayFailure ?? $"{_completedCount} of {activePrompts.Count} variations done — saved to your gallery.");
            _isGenerating = false;
            _cts?.Dispose();
            _cts = null;
            await RefreshAsync();
        }
    }

    private void CancelGeneration()
    {
        _cts?.Cancel();
    }

    private async Task AutoSaveVariationAsync(string imageData, string contentType)
    {
        // Pass tags from the bulk pipeline's analysis (if any). For now this is empty — the
        // /variation endpoint currently doesn't return a per-vision-tags payload, but
        // threading the slot through here keeps the door open for the next pipeline iteration.
        await UserImageSave.SaveResultFromBase64Async(imageData, contentType, UserImageKind.BulkVariation, tags: null);
    }

    private void ToggleFavorite(int index)
    {
        if (!_favorites.Add(index))
            _favorites.Remove(index);
        StateHasChanged();
    }

    private async Task DownloadFavorites()
    {
        // Favorites are usually one or two picks — individual saves keep the file names
        // meaningful. Use "Download All as ZIP" for the whole batch.
        foreach (var idx in _favorites.OrderBy(i => i))
            await DownloadImage(idx);
    }

    /// <summary>
    /// Packs every completed variation into one archive. Ten saves used to mean ten
    /// browser download prompts; this makes it one.
    /// </summary>
    private async Task DownloadAllAsZip()
    {
        if (_zipping) return;
        var entries = _completedResults
            .Select(r => new { name = $"variation-{r.Index + 1}.png", url = r.ImageUrl! })
            .ToArray();
        if (entries.Length == 0) return;

        _zipping = true;
        try
        {
            var written = await Js.InvokeAsync<int>("poUx.downloadZip", entries, "poredoimage-variations.zip");
            if (written == 0)
            {
                NotificationService.Notify(NotificationSeverity.Error, "ZIP Failed",
                    "None of the images could be read. Try saving them individually.", duration: 6000);
            }
            else
            {
                // A short count is not a silent truncation: say which images didn't make it.
                var severity = written == entries.Length ? NotificationSeverity.Success : NotificationSeverity.Warning;
                var detail = written == entries.Length
                    ? $"{written} image{(written == 1 ? "" : "s")} packed."
                    : $"{written} of {entries.Length} images packed — the rest could not be read.";
                NotificationService.Notify(severity, "ZIP Ready", detail, duration: 4000);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "ZIP download failed");
            NotificationService.Notify(NotificationSeverity.Error, "ZIP Failed", ex.Message, duration: 6000);
        }
        finally
        {
            _zipping = false;
            StateHasChanged();
        }
    }

    private async Task SavePrompts()
    {
        if (_userId is null)
        {
            NotificationService.Notify(NotificationSeverity.Error, "Not Signed In", "Unable to save: no user ID found. Please sign out and sign back in.", duration: 6000);
            return;
        }
        _isSaving = true;
        try
        {
            // Persist via the BFF — the WASM client never touches table storage directly.
            var resp = await Http.PostAsJsonAsync("/api/bulk-generate/prompts", new SavePromptsRequest(_prompts), SharedJsonOptions.Default);
            resp.EnsureSuccessStatusCode();
            NotificationService.Notify(NotificationSeverity.Success, "Saved", "Prompts saved successfully!", duration: 3000);
        }
        catch (Exception ex)
        {
            NotificationService.Notify(NotificationSeverity.Error, "Save Failed", $"Failed to save prompts: {ex.Message}", duration: 6000);
            Logger.LogError(ex, "Failed to save bulk prompts");
        }
        finally
        {
            _isSaving = false;
        }
    }

    private async Task LoadPrompts()
    {
        if (_userId is null)
        {
            NotificationService.Notify(NotificationSeverity.Error, "Not Signed In", "Unable to load: no user ID found. Please sign out and sign back in.", duration: 6000);
            return;
        }
        try
        {
            var resp = await Http.GetAsync("/api/bulk-generate/prompts");
            if (resp.IsSuccessStatusCode)
            {
                var saved = await resp.Content.ReadFromJsonAsync<string[]>(SharedJsonOptions.Default);
                if (saved is not null)
                {
                    _prompts = saved;
                    NotificationService.Notify(NotificationSeverity.Success, "Loaded", "Prompts loaded from your account.", duration: 3000);
                    return;
                }
            }
            NotificationService.Notify(NotificationSeverity.Info, "No Saved Prompts", "No saved prompts found for your account.", duration: 4000);
        }
        catch (Exception ex)
        {
            NotificationService.Notify(NotificationSeverity.Error, "Load Failed", $"Failed to load prompts: {ex.Message}", duration: 6000);
            Logger.LogError(ex, "Failed to load bulk prompts");
        }
    }

    private async Task DownloadImage(int index)
    {
        var result = _results.ElementAtOrDefault(index);
        if (result?.ImageUrl is null) return;
        await Js.InvokeAsync<bool>("downloadImage", result.ImageUrl, $"bulk-variation-{index + 1}.png");
    }

    // ─── Idea #11 — One-Tap Re-roll x3 ───────────────────────────────
    // A user can click "Re-roll × 3" on any completed slot. The page posts the winning
    // prompt + source image to /api/bulk-generate/reroll, which returns 3 fresh variations
    // (each with a different seed nudge). The user picks one to "accept" — the original slot
    // is replaced with the chosen variation and saved to the gallery like any other result.
    private async Task RerollAsync(int index)
    {
        var source = _results.ElementAtOrDefault(index);
        if (source?.Status != BulkGenerateStatus.Complete || ActiveImage() is not var (imageBytes, imageContentType)) return;
        if (_bulkGallery is null) return;

        _bulkGallery.BeginReroll(index);
        try
        {
            var resp = await Http.PostAsJsonAsync("/api/bulk-generate/reroll",
                new BulkRerollRequest(
                    ImageData: Convert.ToBase64String(imageBytes),
                    ContentType: imageContentType,
                    SeedPrompt: source.Prompt ?? string.Empty,
                    Count: 3), SharedJsonOptions.Default);

            if (!resp.IsSuccessStatusCode)
            {
                var problem = await resp.Content.ReadAsStringAsync();
                NotificationService.Notify(NotificationSeverity.Error, "Re-roll Failed",
                    $"Server returned {(int)resp.StatusCode}: {problem}", duration: 6000);
                return;
            }

            var payload = await resp.Content.ReadFromJsonAsync<BulkRerollResponse>(SharedJsonOptions.Default);
            if (payload is null || payload.Variations.Count == 0)
            {
                NotificationService.Notify(NotificationSeverity.Warning, "Re-roll",
                    "No re-rolls were returned. The model may be busy — try again in a moment.", duration: 5000);
                return;
            }

            _bulkGallery.EndReroll(index, payload.Variations);
            // Re-rolls are billed at generation time regardless of which one the user accepts.
            Cost.RecordImages(payload.Succeeded);
            NotificationService.Notify(NotificationSeverity.Success, "Re-roll Ready",
                $"{payload.Succeeded} of {payload.Requested} variations ready in {payload.ElapsedMs}ms.",
                duration: 4000);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Re-roll failed for slot {Index}", index);
            NotificationService.Notify(NotificationSeverity.Error, "Re-roll Failed", ex.Message, duration: 6000);
        }
        finally
        {
            // Every exit clears the slot's "Re-rolling…" state; before, only success did, so a
            // failed re-roll left the button disabled for good.
            _bulkGallery?.EndReroll(index);
            await RefreshAsync();
        }
    }

    // EventCallback target wired to <BulkGallery OnReroll="...">
    private Task RerollFromSlot(int index) => RerollAsync(index);

    // User accepted a re-roll variation: swap it into the source slot and auto-save.
    private async Task AcceptReroll((int SourceIndex, BulkRerollVariation Variation) payload)
    {
        var (sourceIndex, variation) = payload;
        if (sourceIndex < 0 || sourceIndex >= _results.Count) return;
        if (variation is null || string.IsNullOrEmpty(variation.ImageData)) return;

        _results[sourceIndex].ImageUrl = $"data:{variation.ContentType};base64,{variation.ImageData}";
        _results[sourceIndex].Status = BulkGenerateStatus.Complete;
        _results[sourceIndex].ErrorMessage = null;

        // Fire-and-forget auto-save the accepted variation, mirroring bulk generation behaviour.
        if (_userId is not null)
            _ = AutoSaveVariationAsync(variation.ImageData, variation.ContentType);

        NotificationService.Notify(NotificationSeverity.Success, "Variation Replaced",
            $"Slot #{sourceIndex + 1} now shows the re-rolled image.", duration: 3500);

        await RefreshAsync();
    }
}
