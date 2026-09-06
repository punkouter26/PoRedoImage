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
/// all logic lives here so neither half has to be read through the other.
/// </summary>
public partial class BulkGenerate
{
    private IBrowserFile? _selectedFile;
    private byte[]? _imageBytes;
    private string _imageContentType = "image/jpeg";
    private string? _imagePreviewUrl;
    private string? _uploadError;
    private string[] _prompts = DefaultPrompts.All.ToArray();
    private int _activePromptCount => _prompts.Count(p => !string.IsNullOrWhiteSpace(p));
    private List<BulkGenerateImageResult> _results = [];
    private int _completedCount;
    private bool _isGenerating;
    private bool _isSaving;
    private HashSet<int> _favorites = [];
    private string? _userId;
    private MyImagesGallery? _gallery;
    private BulkGallery? _bulkGallery;
    private CancellationTokenSource? _cts;
    private bool _zipping;

    /// <summary>Completed slots that actually carry an image — the ZIP's contents.</summary>
    private List<BulkGenerateImageResult> _completedResults =>
        [.. _results.Where(r => r.Status == BulkGenerateStatus.Complete && r.ImageUrl is not null)];

    private sealed record BulkSavedState(BulkGenerateImageResult[] Results);

    // Hoisted out of OnAfterRenderAsync so the JsonSerializerOptions graph (with its metadata
    // caches) is built once instead of per circuit-restore attempt.
    private static readonly System.Text.Json.JsonSerializerOptions RestoreJsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    protected override async Task OnInitializedAsync()
    {
        var authState = await AuthStateProvider.GetAuthenticationStateAsync();
        _userId = authState.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        // Cross-page state: record this as the last visited feature route so
        // the Active Image Bar can deep-link back here.
        SessionService.RecordFeatureVisit("/bulk-generate");

        // A prompt handed over from Style Director lands in slot 1, so the run the user just
        // asked for is the first one generated. Taken once: coming back to this page later
        // must not silently re-seed a prompt they have since edited or cleared.
        var staged = SessionService.TakeStagedPrompt();
        if (staged is not null)
        {
            _prompts[0] = staged;
            NotificationService.Notify(
                NotificationSeverity.Success,
                "Prompt Loaded",
                "Style Director's prompt is in slot 1. Upload or keep your photo, then Generate.",
                duration: 5000);
        }

        // Active image-gen provider + indicative per-image pricing, shared with the footer chip
        // so the page total and the session total can't disagree. Non-critical: a failure just
        // hides the pricing note.
        await Cost.EnsureLoadedAsync();

        if (SessionService.HasImage && _imagePreviewUrl is null)
        {
            _imagePreviewUrl = SessionService.PreviewUrl;
            if (SessionService.Bytes is not null)
            {
                _imageBytes = SessionService.Bytes;
            }
            else if (_imagePreviewUrl is not null)
            {
                try
                {
                    var idx = _imagePreviewUrl.IndexOf(',');
                    if (idx >= 0) _imageBytes = Convert.FromBase64String(_imagePreviewUrl[(idx + 1)..]);
                }
                catch { /* bytes unavailable */ }
            }
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Restore bulk generation results from localStorage after circuit reconnect or tab reopen
        if (firstRender && _results.Count == 0)
        {
            try
            {
                var savedJson = await JSRuntime.InvokeAsync<string?>("bulkStateManager.load");
                if (!string.IsNullOrEmpty(savedJson))
                {
                    var state = System.Text.Json.JsonSerializer.Deserialize<BulkSavedState>(savedJson, RestoreJsonOpts);
                    if (state?.Results?.Length > 0)
                    {
                        _results = [.. state.Results];
                        _completedCount = _results.Count(r => r.Status == BulkGenerateStatus.Complete);
                        NotificationService.Notify(NotificationSeverity.Info, "Session Restored", $"Restored {_completedCount} result(s) from previous session.", duration: 4000);
                        StateHasChanged();
                    }
                }
            }
            catch { /* non-critical — ignore restore errors */ }
        }
    }

    private async Task LoadFile(InputFileChangeEventArgs e)
    {
        _selectedFile = e.File;
        _uploadError = null;
        _imagePreviewUrl = null;
        _imageBytes = null;
        _results = [];
        _completedCount = 0;

        var (result, error) = await ImageLoadHelper.LoadAsync(_selectedFile);
        if (error is not null) { _uploadError = error; _selectedFile = null; return; }

        _imageBytes = result!.Bytes;
        _imageContentType = result.ContentType;
        _imagePreviewUrl = result.PreviewUrl;
        SessionService.SetImage(result!.PreviewUrl, result.ContentType, _selectedFile!.Name, result.Bytes);
        if (_userId is not null && result.Bytes is not null)
            _ = AutoSaveOriginalAsync(result.Bytes, result.ContentType, _selectedFile!.Name);
        StateHasChanged();
    }

    /// <summary>Clipboard paste / drop-anywhere intake — mirrors <see cref="LoadFile"/>.</summary>
    private void HandleImageIntake(IntakeImage payload)
    {
        if (payload.Error is not null) { _uploadError = payload.Error; StateHasChanged(); return; }
        var bytes = payload.Decode();
        if (bytes is null) { _uploadError = "The pasted image could not be read."; StateHasChanged(); return; }

        _selectedFile = null;
        _uploadError = null;
        _results = [];
        _completedCount = 0;
        _favorites = [];
        _imageBytes = bytes;
        _imageContentType = payload.ContentType ?? "image/png";
        _imagePreviewUrl = $"data:{_imageContentType};base64,{payload.Base64}";

        var fileName = payload.FileName ?? "pasted-image.png";
        SessionService.SetImage(_imagePreviewUrl, _imageContentType, fileName, bytes);
        if (_userId is not null)
            _ = AutoSaveOriginalAsync(bytes, _imageContentType, fileName);

        NotificationService.Notify(NotificationSeverity.Success,
            payload.Source == "drop" ? "Image dropped" : "Image pasted",
            $"{fileName} is ready to generate from.", duration: 2500);
        StateHasChanged();
    }

    private async Task StartGeneration()
    {
        if (_imageBytes is null || _imagePreviewUrl is null) return;

        // Clear any previously saved session state before starting a new batch
        await JSRuntime.InvokeVoidAsync("bulkStateManager.clear");
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

        try
        {
            // Use GPT-4o vision via API to get a detailed physical description of the person
            var descResp = await Http.PostAsJsonAsync("/api/bulk-generate/describe",
                new BulkDescribeRequest(Convert.ToBase64String(_imageBytes), _imageContentType), SharedJsonOptions.Default);
            descResp.EnsureSuccessStatusCode();
            var descResult = await descResp.Content.ReadFromJsonAsync<BulkDescribeResponse>(SharedJsonOptions.Default);
            Cost.RecordVision(1);
            var description = descResult?.Description ?? string.Empty;

            if (string.IsNullOrEmpty(description))
            {
                Logger.LogWarning("Bulk Generate: person description unavailable — proceeding without <PERSON> substitution");
                NotificationService.Notify(NotificationSeverity.Warning, "Limited Mode",
                    "Person description is unavailable — generating with original prompts (AI key may need updating).",
                    duration: 7000);
            }
            else
            {
                Logger.LogInformation("Bulk Generate: Vision description acquired: {Description}", description);
            }

            // ── One request, streamed ────────────────────────────────────────────
            // This was a `for` loop of one-at-a-time POSTs, each re-uploading the whole source
            // image: ten sequential round-trips for a batch of ten, and roughly 53MB of base64
            // upload for a 4MB photo. The server now fans out under its own concurrency cap and
            // streams each slot back as NDJSON, so the board still fills in one card at a time —
            // it just stops taking three times longer than it needs to.
            //
            // Slots complete OUT OF ORDER under concurrency, which is why each line carries its
            // index rather than relying on arrival sequence.
            var safeDescription = SanitizeDescription(description);
            var finalPrompts = activePrompts
                .Select(p => string.IsNullOrEmpty(safeDescription)
                    ? p.prompt
                    : p.prompt.Replace(DefaultPrompts.PersonToken, safeDescription, StringComparison.Ordinal))
                .ToArray();

            foreach (var slot in _results) slot.Status = BulkGenerateStatus.Processing;
            StateHasChanged();

            using var batchRequest = new HttpRequestMessage(HttpMethod.Post, "/api/bulk-generate/batch")
            {
                Content = JsonContent.Create(
                    new BulkBatchRequest(
                        Convert.ToBase64String(_imageBytes),
                        _imageContentType,
                        finalPrompts,
                        AiSelection.Get(AiCapability.GenerateImage)),
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
                    _ = Audio.FailureAsync();
                }
                else
                {
                    slot.Status = BulkGenerateStatus.Complete;
                    slot.Prompt = activePrompts[item.Index].prompt;
                    slot.ImageUrl = $"data:{item.ContentType};base64,{item.ImageData}";
                    _completedCount++;
                    Cost.RecordImages();
                    _ = Audio.TickAsync();
                    if (_userId is not null)
                        _ = AutoSaveVariationAsync(item.ImageData, item.ContentType);
                }

                StateHasChanged();
                // Persist results to localStorage so they survive circuit disconnection
                _ = JSRuntime.InvokeVoidAsync("bulkStateManager.save",
                    System.Text.Json.JsonSerializer.Serialize(new BulkSavedState(_results.ToArray()), RestoreJsonOpts)).AsTask();
            }
            // Any slot the server never reported is a slot that did not land — mark it rather than
            // leaving it spinning forever.
            foreach (var slot in _results.Where(r => r.Status == BulkGenerateStatus.Processing))
            {
                slot.Status = BulkGenerateStatus.Failed;
                slot.ErrorMessage = "No result was returned for this variation.";
            }

            if (!_cts.Token.IsCancellationRequested)
            {
                if (_completedCount > 0)
                    NotificationService.Notify(NotificationSeverity.Success, "Done!", $"Completed {_completedCount} of {activePrompts.Count} variation{(activePrompts.Count == 1 ? "" : "s")}!", duration: 5000);
                else
                    NotificationService.Notify(NotificationSeverity.Error, "All Failed", "All generations failed. Check each card for details.", duration: 7000);

                // Audio cue: success arpeggio when at least one variation completed, otherwise failure.
                _ = _completedCount > 0 ? Audio.SuccessAsync() : Audio.FailureAsync();
            }
        }
        catch (OperationCanceledException)
        {
            NotificationService.Notify(NotificationSeverity.Warning, "Cancelled", "Generation was cancelled.", duration: 4000);
        }
        catch (Exception ex)
        {
            NotificationService.Notify(NotificationSeverity.Error, "Error", $"Error during generation: {ex.Message}", duration: 7000);
            Logger.LogError(ex, "Bulk Generate failed");
        }
        finally
        {
            _isGenerating = false;
            _cts?.Dispose();
            _cts = null;
            try { StateHasChanged(); } catch (ObjectDisposedException) { }
        }
    }

    private void CancelGeneration()
    {
        _cts?.Cancel();
    }

    private void ApplyGalleryImage(MyImagesGallery.GalleryItem item)
    {
        _selectedFile = null;
        _imagePreviewUrl = SessionService.PreviewUrl;
        _imageBytes = SessionService.Bytes;
        _imageContentType = item.ContentType;
        _uploadError = null;
        _results = [];
        _completedCount = 0;
        _favorites = [];
        StateHasChanged();
    }

    private async Task AutoSaveOriginalAsync(byte[] bytes, string contentType, string fileName)
    {
        // Idempotent save with a Retry-toast on failure. The fire-and-forget at the call site
        // keeps the upload UI snappy; the toast handles the (rare) failure case without silently
        // dropping the image like the old Http.PostAsJsonAsync version did.
        var savedId = await UserImageSave.SaveOriginalAsync(bytes, contentType, fileName, tags: null);
        if (savedId is not null && _gallery is not null)
            await InvokeAsync(_gallery.LoadAsync);
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
            var written = await JSRuntime.InvokeAsync<int>("poUx.downloadZip", entries, "poredoimage-variations.zip");
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
        await JSRuntime.InvokeAsync<bool>("downloadImage", result.ImageUrl, $"bulk-variation-{index + 1}.png");
    }

    // ─── Idea #11 — One-Tap Re-roll x3 ───────────────────────────────
    // A user can click "Re-roll × 3" on any completed slot. The page posts the winning
    // prompt + source image to /api/bulk-generate/reroll, which returns 3 fresh variations
    // (each with a different seed nudge). The user picks one to "accept" — the original slot
    // is replaced with the chosen variation and saved to the gallery like any other result.
    private async Task RerollAsync(int index)
    {
        var source = _results.ElementAtOrDefault(index);
        if (source?.Status != BulkGenerateStatus.Complete || _imageBytes is null) return;
        if (_bulkGallery is null) return;

        _bulkGallery.BeginReroll(index);
        try
        {
            var resp = await Http.PostAsJsonAsync("/api/bulk-generate/reroll",
                new BulkRerollRequest(
                    ImageData: Convert.ToBase64String(_imageBytes),
                    ContentType: _imageContentType,
                    SeedPrompt: source.Prompt ?? string.Empty,
                    Count: 3,
                    ImageGenModelId: AiSelection.Get(AiCapability.GenerateImage)), SharedJsonOptions.Default);

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
            try { await InvokeAsync(StateHasChanged); } catch (ObjectDisposedException) { }
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

        try { await InvokeAsync(StateHasChanged); } catch (ObjectDisposedException) { }
    }

    /// <summary>
    /// BOMB-3 mitigation: caps the GPT-4o vision description to 200 chars and strips
    /// HTML/markdown so it can be safely substituted into a prompt template without
    /// token-quota drain or reflected-XSS risk (Po2Logic audit).
    /// </summary>
    private static string? SanitizeDescription(string? description)
    {
        if (string.IsNullOrEmpty(description)) return null;
        // 1. Length cap (200 chars — ~50 tokens, plenty for "young woman with brown hair, blue eyes")
        const int MaxLen = 200;
        var trimmed = description.Length > MaxLen ? description[..MaxLen] : description;
        // 2. Collapse newlines (descriptions are noun-phrase only — newlines are suspicious)
        trimmed = trimmed.Replace('\n', ' ').Replace('\r', ' ');
        // 3. Strip angle brackets and HTML tags — defense-in-depth even though we trust GPT-4o
        var span = trimmed.AsSpan();
        var buf = new System.Text.StringBuilder(span.Length);
        var inTag = false;
        foreach (var ch in span)
        {
            if (ch == '<') { inTag = true; continue; }
            if (ch == '>') { inTag = false; continue; }
            if (!inTag) buf.Append(ch);
        }
        return buf.ToString().Trim();
    }
}
