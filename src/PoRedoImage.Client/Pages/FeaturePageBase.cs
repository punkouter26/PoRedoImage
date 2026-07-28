using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using PoRedoImage.Client.LocalAi;
using PoRedoImage.Client.Models;
using PoRedoImage.Shared.DTOs;
using PoRedoImage.Client.Shared;
using Radzen;
using System.Security.Claims;

namespace PoRedoImage.Client.Pages;

/// <summary>
/// Shared base component for feature pages (ImageRegeneration, MemeGeneration).
/// Centralises upload, gallery selection, auto-save, and progress state that is
/// identical across both pages, eliminating ~70 lines of duplication.
/// </summary>
public abstract class FeaturePageBase : ComponentBase
{
    [Inject] protected HttpClient Http { get; set; } = default!;
    [Inject] protected ImageSessionService SessionService { get; set; } = default!;
    [Inject] protected AuthenticationStateProvider AuthStateProvider { get; set; } = default!;
    [Inject] protected ILoggerFactory LoggerFactory { get; set; } = default!;
    [Inject] protected NotificationService NotificationService { get; set; } = default!;
    [Inject] protected NavigationManager NavigationManager { get; set; } = default!;
    [Inject] protected AiSelectionState AiSelection { get; set; } = default!;
    [Inject] protected LocalAiService LocalAi { get; set; } = default!;

    private ILogger? _logger;
    protected ILogger Logger => _logger ??= LoggerFactory.CreateLogger(GetType());

    protected IBrowserFile? selectedFile;
    protected string? imagePreviewUrl;
    protected string? errorMessage;
    protected bool isProcessing;
    protected bool isComplete;
    protected int progressPercentage;
    protected string progressMessage = string.Empty;
    protected MyImagesGallery? _gallery;
    protected string? _userId;

    protected bool canProcessImage => selectedFile != null || imagePreviewUrl != null;

    /// <summary>Strips the <c>data:...;base64,</c> prefix from a preview URL, returning raw base64.</summary>
    protected static string ExtractBase64(string previewUrl)
    {
        var idx = previewUrl.IndexOf(";base64,", StringComparison.OrdinalIgnoreCase);
        return idx >= 0 ? previewUrl[(idx + 8)..] : previewUrl;
    }

    protected override async Task OnInitializedAsync()
    {
        if (SessionService.HasImage)
            imagePreviewUrl = SessionService.PreviewUrl;
        var auth = await AuthStateProvider.GetAuthenticationStateAsync();
        _userId = auth.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        // Cross-page state (#4): record that this page is being entered.
        // The route is what the Active Image Bar uses to deep-link back, so
        // it must be the page's canonical @page URL — NOT a relative href.
        var route = NavigationManager.Uri;
        var path = new Uri(route).AbsolutePath;
        SessionService.RecordFeatureVisit(path);
    }

    protected async Task LoadFile(InputFileChangeEventArgs e)
    {
        selectedFile = e.File;
        errorMessage = null;
        imagePreviewUrl = null;

        var (result, error) = await ImageLoadHelper.LoadAsync(selectedFile);
        if (error is not null) { errorMessage = error; selectedFile = null; return; }

        imagePreviewUrl = result!.PreviewUrl;
        SessionService.SetImage(result!.PreviewUrl, result.ContentType, selectedFile.Name, result.Bytes);
        if (_userId is not null && result.Bytes is not null)
            _ = AutoSaveOriginalAsync(result.Bytes, result.ContentType, selectedFile.Name);
        StateHasChanged();
        Logger.LogInformation("Image loaded: {Name}, {KB:F1} KB", selectedFile.Name, selectedFile.Size / 1024.0);
    }

    protected void HandleGalleryImage(MyImagesGallery.GalleryItem item)
    {
        selectedFile = null;
        imagePreviewUrl = SessionService.PreviewUrl;
        isComplete = false;
        errorMessage = null;
        OnGalleryImageSelected();
        StateHasChanged();
    }

    /// <summary>Called by HandleGalleryImage so derived pages can clear their own result state.</summary>
    protected virtual void OnGalleryImageSelected() { }

    protected async Task AutoSaveOriginalAsync(byte[] bytes, string contentType, string fileName)
    {
        try
        {
            await Http.PostAsJsonAsync("/api/user-images/original",
                new SaveOriginalRequest(Convert.ToBase64String(bytes), contentType, fileName));
            if (_gallery is not null) await _gallery.LoadAsync();
        }
        catch (Exception ex) { Logger.LogWarning(ex, "Auto-save original failed"); }
    }

    /// <summary>
    /// Builds an analysis request carrying the session's provider selections.
    /// </summary>
    /// <remarks>
    /// Centralised here rather than duplicated in ImageRegeneration and MemeGeneration: both pages
    /// build the same request and would otherwise drift the moment a new field is added. Async and
    /// taking the base64 image data (not raw bytes) because Task 6 runs browser-local vision inside
    /// the browser branch, deriving bytes from <paramref name="imageData"/> only when needed there.
    /// </remarks>
    protected virtual async Task<ImageAnalysisRequest> BuildAnalysisRequestAsync(
        string imageData,
        string contentType,
        string fileName,
        int descriptionLength,
        ProcessingMode mode,
        CancellationToken ct = default)
    {
        var request = new ImageAnalysisRequest
        {
            ImageData = imageData,
            ContentType = contentType,
            FileName = fileName,
            DescriptionLength = descriptionLength,
            Mode = mode,
            ModelId = AiSelection.Get(AiCapability.AnalyzeImage),
            ImageGenModelId = AiSelection.Get(AiCapability.GenerateImage),
        };

        if (!AiSelection.GetOption(AiCapability.AnalyzeImage).ExecutesInBrowser)
        {
            return request;
        }

        var imageBytes = Convert.FromBase64String(imageData);

        var outcome = await LocalAi.DescribeImageAsync(
            imageBytes,
            prompt: "Describe this image and list its subjects.",
            progress: new Progress<LocalInferenceStatus>(OnLocalProgress),
            ct: ct);

        request.PrecomputedDescription = outcome.Text;
        request.PrecomputedTags = ExtractTags(outcome.Text);
        return request;
    }

    /// <summary>
    /// Derives coarse tags from a local model's free-text description. Browser vision models emit
    /// prose, not a tag list, and the server's meme branch needs tags to caption from.
    /// </summary>
    private static IReadOnlyList<string> ExtractTags(string description) =>
        [.. description
            .Split([' ', ',', '.', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => w.Length > 3)
            .Select(w => w.ToLowerInvariant())
            .Distinct()
            .Take(10)];

    /// <summary>Surfaces local-inference progress through the existing progress UI.</summary>
    private void OnLocalProgress(LocalInferenceStatus status)
    {
        var message = status.Stage switch
        {
            LocalStage.Probing => "Checking your device…",
            LocalStage.Downloading => $"Downloading model… {status.LoadPercent ?? 0}%",
            LocalStage.Loading => "Loading model…",
            LocalStage.Running => "Analyzing on your device…",
            _ => null,
        };

        if (message is null) return;

        progressMessage = message;
        InvokeAsync(StateHasChanged);
    }
}
