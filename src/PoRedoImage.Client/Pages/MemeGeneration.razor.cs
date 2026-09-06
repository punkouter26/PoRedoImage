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
/// Code-behind for <c>MemeGeneration.razor</c>. The markup file keeps its directives and template;
/// all logic lives here so neither half has to be read through the other.
/// </summary>
public partial class MemeGeneration : FeaturePageBase
{
    private ImageAnalysisResponse? analysisResult;
    private string? MemeImageUrl => string.IsNullOrEmpty(analysisResult?.MemeImageData)
        ? null
        : $"data:image/png;base64,{analysisResult.MemeImageData}";

    // ── Idea #17 — Meme Template Library state ─────────────────────
    private bool _useTemplate;
    private IReadOnlyList<MemeTemplateDto>? _templates;
    private MemeTemplateDto? _selectedTemplate;
    private string? _selectedTemplateId;
    private List<string> _zoneTexts = [];

    // Hoisted: the analyze response can carry a 700+ KB base64 meme, and rebuilding the
    // JsonSerializerOptions graph on every call would force fresh metadata caches.
    private static readonly System.Text.Json.JsonSerializerOptions AnalyzeJsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = 64,
    };

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        // Eager-load the catalog so toggling the switch is instant.
        _ = LoadTemplatesAsync();
    }

    private async Task LoadTemplatesAsync()
    {
        try
        {
            _templates = await Http.GetFromJsonAsync<IReadOnlyList<MemeTemplateDto>>("/api/meme-templates", SharedJsonOptions.Default);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to load meme templates");
            _templates = [];
        }
        StateHasChanged();
    }

    private void OnTemplateToggleChanged(ChangeEventArgs e)
    {
        _useTemplate = e.Value is bool b && b;
        if (_useTemplate && _templates is null)
            _ = LoadTemplatesAsync();
    }

    private void SelectTemplate(string id)
    {
        _selectedTemplateId = id;
        _selectedTemplate = _templates?.FirstOrDefault(t => t.Id == id);
        if (_selectedTemplate is not null)
        {
            // Reset the per-zone text inputs whenever a new template is picked.
            _zoneTexts = _selectedTemplate.Zones.Select(z => string.Empty).ToList();
        }
    }

    private void UpdateZoneText(int index, string value)
    {
        while (_zoneTexts.Count <= index) _zoneTexts.Add(string.Empty);
        _zoneTexts[index] = value;
    }

    protected override void OnGalleryImageSelected()
    {
        analysisResult = null;
    }

    private async Task AutoSaveMemeAsync(string memeBase64, string contentType)
    {
        var tags = analysisResult?.Tags;
        var savedId = await UserImageSave.SaveResultFromBase64Async(memeBase64, contentType, UserImageKind.Meme, tags);
        if (savedId is not null && _gallery is not null)
            await _gallery.LoadAsync();
    }

    /// <summary>Studio "Surprise me" hand-off — the photo is all the AI-caption path needs.</summary>
    protected override Task? AutoStartAsync() => _useTemplate ? null : ProcessImage();

    private async Task ProcessImage()
    {
        if (imagePreviewUrl == null) return;

        // ── Idea #17 — Template branch (no AI, runs locally) ──────
        if (_useTemplate)
        {
            await ProcessTemplateAsync();
            return;
        }

        try
        {
            isProcessing = true;
            errorMessage = null;
            progressMessage = "Preparing image for analysis...";
            StateHasChanged();

            var base64Data = ExtractBase64(imagePreviewUrl);

            // The local step (§ai-service-pickers finding #3) gets its own, more generous budget:
            // first-run browser-local inference includes a ~230 MB model download plus WASM/WebGPU
            // load, which a 3-minute budget shared with the HTTP call could exhaust on a slow
            // connection. The 3-minute HTTP CTS below is created only once the request is built, so
            // it covers just the round trip it was originally scoped to.
            using var localCts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            var request = await TryBuildAnalysisRequestAsync(
                imageData: base64Data,
                contentType: selectedFile?.ContentType ?? SessionService.ContentType ?? "image/jpeg",
                fileName: selectedFile?.Name ?? SessionService.FileName ?? "image.jpg",
                descriptionLength: 350,
                mode: ProcessingMode.MemeGeneration,
                ct: localCts.Token);
            if (request is null) return;

            progressMessage = "Analyzing image...";
            StateHasChanged();

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            try
            {
                var httpResponse = await Http.PostAsJsonAsync("api/images/analyze", request, SharedJsonOptions.Default, cts.Token);

                if (!httpResponse.IsSuccessStatusCode)
                {
                    if (await HandleAnalyzeErrorAsync(httpResponse)) return;
                }
                else
                {
                    // The response can carry a 700+ KB base64 meme + an optional regenerated image,
                    // which approaches Blazor WASM's default deserialization budget. Bump the limit and
                    // explicitly use System.Text.Json to keep behaviour deterministic across hosts.
                    // ReadAsStringAsync first so we can show progress and avoid a stuck-progress state
                    // when deserialization of a multi-MB payload takes a noticeable moment on the WASM side.
                    progressMessage = "Decoding meme…";
                    try { await InvokeAsync(StateHasChanged); } catch (ObjectDisposedException) { }
                    var rawJson = await httpResponse.Content.ReadAsStringAsync(cts.Token);
                    progressMessage = "Rendering result…";
                    try { await InvokeAsync(StateHasChanged); } catch (ObjectDisposedException) { }
                    analysisResult = System.Text.Json.JsonSerializer.Deserialize<ImageAnalysisResponse>(rawJson, AnalyzeJsonOpts);
                    if (analysisResult == null)
                        throw new InvalidOperationException("No response received from API.");

                    Cost.RecordVision(1);
                    Cost.RecordTextReasoning(1);

                    if (_userId is not null && !string.IsNullOrEmpty(analysisResult.MemeImageData))
                        _ = AutoSaveMemeAsync(analysisResult.MemeImageData, "image/png");
                }
            }
            catch (OperationCanceledException ex)
            {
                Logger.LogError(ex, "Request timed out after 3 minutes");
                errorMessage = "Meme generation timed out. Please try again with a different image.";
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error calling API");
                errorMessage = $"Error generating meme: {ex.Message}";
            }
            finally
            {
                progressMessage = string.IsNullOrEmpty(errorMessage) ? "Meme generated!" : "Generation failed.";
                isProcessing = false;
                isComplete = string.IsNullOrEmpty(errorMessage);
                if (string.IsNullOrEmpty(errorMessage))
                    NotificationService.Notify(NotificationSeverity.Success, "Meme Ready!", "Your meme has been generated.", duration: 4000);
                // Force a re-render on the UI thread so the spinner can't get stuck if the
                // deserialization of the 13MB-base64 response left the circuit waiting.
                try { await InvokeAsync(StateHasChanged); } catch (ObjectDisposedException) { }
                try { StateHasChanged(); } catch (ObjectDisposedException) { }
            }
        }
        catch (Exception ex)
        {
            errorMessage = $"Error generating meme: {ex.Message}";
            isProcessing = false;
            Logger.LogError(ex, "Error generating meme");
            try { await InvokeAsync(StateHasChanged); } catch (ObjectDisposedException) { }
        }
    }

    /// <summary>
    /// Idea #17 — Renders a meme from a selected template + user-provided zone texts.
    /// Pure local render — no AI cost, no rate-limit, near-instant. Reuses the
    /// existing <see cref="ImageAnalysisResponse"/> shape so the result panel renders
    /// the template-rendered output identically to the AI-meme output.
    /// </summary>
    private async Task ProcessTemplateAsync()
    {
        if (_selectedTemplate is null)
        {
            errorMessage = "Pick a template first.";
            return;
        }
        if (_zoneTexts.All(string.IsNullOrWhiteSpace))
        {
            errorMessage = "Fill in at least one text zone before rendering.";
            return;
        }

        try
        {
            isProcessing = true;
            errorMessage = null;
            progressMessage = $"Rendering '{_selectedTemplate.Name}'…";
            StateHasChanged();

            var base64Data = ExtractBase64(imagePreviewUrl!);
            var request = new MemeTemplateRenderRequest(
                ImageData: base64Data,
                ContentType: selectedFile?.ContentType ?? SessionService.ContentType ?? "image/jpeg",
                TemplateId: _selectedTemplate.Id,
                ZoneTexts: _zoneTexts);

            var response = await Http.PostAsJsonAsync("/api/meme-templates/render", request, SharedJsonOptions.Default);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                errorMessage = $"Template render failed: {body}";
                return;
            }

            var rendered = await response.Content.ReadFromJsonAsync<MemeTemplateRenderResponse>(SharedJsonOptions.Default);
            if (rendered is null || string.IsNullOrEmpty(rendered.ImageData))
            {
                errorMessage = "Template render returned no image.";
                return;
            }

            // Map into the existing ImageAnalysisResponse so the result panel shows it.
            analysisResult = new ImageAnalysisResponse
            {
                MemeImageData = rendered.ImageData,
                MemeCaption = string.Join(" / ",
                    _zoneTexts.Where(t => !string.IsNullOrWhiteSpace(t))),
                RegeneratedImageContentType = rendered.ContentType,
                Metrics = new ProcessingMetricsDto
                {
                    ImageAnalysisTimeMs = 0,
                    DescriptionGenerationTimeMs = 0,
                    ImageRegenerationTimeMs = rendered.ElapsedMs
                }
            };

            if (_userId is not null)
                _ = AutoSaveMemeAsync(rendered.ImageData, rendered.ContentType);


            NotificationService.Notify(NotificationSeverity.Success, "Template Ready!",
                $"'{_selectedTemplate.Name}' rendered in {rendered.ElapsedMs}ms.", duration: 3500);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Template render failed");
            errorMessage = $"Template render failed: {ex.Message}";
        }
        finally
        {
            progressMessage = string.IsNullOrEmpty(errorMessage) ? "Done!" : "Failed.";
            isProcessing = false;
            isComplete = string.IsNullOrEmpty(errorMessage);
            try { StateHasChanged(); } catch (ObjectDisposedException) { }
        }
    }

    private async Task DownloadOriginal()
    {
        if (imagePreviewUrl == null) return;
        var ok = await JSRuntime.InvokeAsync<bool>("downloadImage", imagePreviewUrl, selectedFile?.Name ?? "original.jpg");
        if (!ok) errorMessage = "There was a problem downloading the image.";
        else Logger.LogInformation("Original image download initiated");
    }

    private async Task DownloadMeme()
    {
        var url = MemeImageUrl;
        if (url == null) return;
        var ok = await JSRuntime.InvokeAsync<bool>("downloadImage", url, "meme-" + (selectedFile?.Name ?? "image.png"));
        if (!ok) errorMessage = "There was a problem downloading the meme image.";
        else Logger.LogInformation("Meme image download initiated");
    }

}
