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

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        // Eager-load the catalog so switching to Template is instant.
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

    private void SetMode(bool useTemplate)
    {
        _useTemplate = useTemplate;
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

    protected override void OnImageChanged()
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

        // Template branch: no AI, renders locally.
        if (_useTemplate)
        {
            await ProcessTemplateAsync();
            return;
        }

        analysisResult = await AnalyzeAsync(350, ProcessingMode.MemeGeneration);
        if (analysisResult is null) return;

        if (_userId is not null && !string.IsNullOrEmpty(analysisResult.MemeImageData))
            _ = AutoSaveMemeAsync(analysisResult.MemeImageData, "image/png");

        await SucceedAsync("Meme Ready!", "Your meme has been generated.");
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
            await RefreshAsync();
        }
    }

    private async Task DownloadMeme()
    {
        var url = MemeImageUrl;
        if (url == null) return;
        var ok = await Js.InvokeAsync<bool>("downloadImage", url, "meme-" + (selectedFile?.Name ?? "image.png"));
        if (!ok) errorMessage = "There was a problem downloading the meme image.";
        else Logger.LogInformation("Meme image download initiated");
    }

}
