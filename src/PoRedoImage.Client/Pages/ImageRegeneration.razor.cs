using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using PoRedoImage.Client.Models;
using PoRedoImage.Domain.Entities;
using PoRedoImage.Shared.DTOs;

namespace PoRedoImage.Client.Pages;

/// <summary>
/// Code-behind for <c>ImageRegeneration.razor</c> — every other feature page already kept its logic
/// out of the markup; this one carried a 160-line <c>@code</c> block.
/// </summary>
public partial class ImageRegeneration : FeaturePageBase
{
    private string? regeneratedImageUrl;
    private string? imageDescription;
    private int descriptionLength = 350;
    private ImageAnalysisResponse? analysisResult;
    private bool _descExpanded;
    private StyleRecipe? _activeRecipe;

    private static readonly (int Value, string Label)[] DetailPresets =
        [(200, "Quick"), (350, "Standard"), (500, "Detailed")];

    private void OnRecipeChanged(ChangeEventArgs e) =>
        _activeRecipe = StyleRecipeCatalog.All.FirstOrDefault(r => r.Id == e.Value?.ToString());

    protected override void OnImageChanged()
    {
        regeneratedImageUrl = null;
        analysisResult = null;
    }

    /// <summary>Studio "Surprise me" hand-off — the photo is all this page needs.</summary>
    protected override Task? AutoStartAsync() => ProcessImage();

    private async Task AutoSaveResultAsync(byte[] bytes, string contentType)
    {
        // Vision tags ride along so the gallery can filter by content later.
        var savedId = await UserImageSave.SaveResultAsync(bytes, contentType, UserImageKind.Regeneration, analysisResult?.Tags);
        if (savedId is not null && _gallery is not null)
            await _gallery.LoadAsync();
    }

    private async Task ProcessImage()
    {
        analysisResult = await AnalyzeAsync(descriptionLength, ProcessingMode.ImageRegeneration, ApplyStyle);
        if (analysisResult is null) return;

        imageDescription = analysisResult.Description;
        SessionService.RecordFeatureVisit("/image-regeneration", analysisResult.Description);
        regeneratedImageUrl = !string.IsNullOrEmpty(analysisResult.RegeneratedImageData)
            ? $"data:{analysisResult.RegeneratedImageContentType};base64,{analysisResult.RegeneratedImageData}"
            : null;

        if (!string.IsNullOrEmpty(analysisResult.RegeneratedImageData))
        {
            Cost.RecordImages(1);
            if (_userId is not null)
                _ = AutoSaveResultAsync(
                    Convert.FromBase64String(analysisResult.RegeneratedImageData),
                    analysisResult.RegeneratedImageContentType);
        }

        await SucceedAsync("Done!", "Your regenerated image is ready.");
    }

    /// <summary>
    /// The style recipe and any prompt staged by another page ride in their own field. Folding them
    /// into PrecomputedDescription told the server the photo was already analysed, so any style
    /// skipped the vision pass and regenerated from the style text alone.
    /// </summary>
    private void ApplyStyle(ImageAnalysisRequest request)
    {
        var staged = SessionService.TakeStagedPrompt();
        var addition = string.Join(", ",
            new[] { staged, _activeRecipe?.PromptSnippet }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (!string.IsNullOrWhiteSpace(addition))
            request.StyleDirective = addition;
    }

    private async Task DownloadRegenerated()
    {
        if (regeneratedImageUrl == null) return;
        var ok = await Js.InvokeAsync<bool>("downloadImage", regeneratedImageUrl, "regenerated-" + (selectedFile?.Name ?? "image.png"));
        if (!ok) errorMessage = "There was a problem downloading the regenerated image.";
    }
}
