using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using PoRedoImage.Client.LocalAi;
using PoRedoImage.Client.Models;
using PoRedoImage.Client.Services;
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
    [Inject] protected UserImageSaveService UserImageSave { get; set; } = default!;

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

        AdoptImage(result!.PreviewUrl, result.Bytes, result.ContentType, selectedFile.Name);
        StateHasChanged();
        Logger.LogInformation("Image loaded: {Name}, {KB:F1} KB", selectedFile.Name, selectedFile.Size / 1024.0);
    }

    /// <summary>
    /// Accepts an image that arrived via clipboard paste or a window-level drop
    /// (see <see cref="IntakeImage"/>), applying the same session + auto-save path as an upload.
    /// </summary>
    protected void HandleImageIntake(IntakeImage payload)
    {
        if (payload.Error is not null) { errorMessage = payload.Error; StateHasChanged(); return; }

        var bytes = payload.Decode();
        if (bytes is null) { errorMessage = "The pasted image could not be read."; StateHasChanged(); return; }

        var contentType = payload.ContentType ?? "image/png";
        var fileName = payload.FileName ?? "pasted-image.png";

        // A pasted image has no IBrowserFile; clearing selectedFile keeps the two intake paths
        // from disagreeing about which file the page is currently working on.
        selectedFile = null;
        errorMessage = null;
        isComplete = false;
        AdoptImage($"data:{contentType};base64,{payload.Base64}", bytes, contentType, fileName);
        OnGalleryImageSelected(); // clears derived result state on the concrete page

        NotificationService.Notify(NotificationSeverity.Success,
            payload.Source == "drop" ? "Image dropped" : "Image pasted",
            $"{fileName} is ready to process.", duration: 2500);
        StateHasChanged();
    }

    /// <summary>
    /// Makes <paramref name="bytes"/> the page's and the session's active image and kicks off the
    /// gallery auto-save. Shared by the upload, paste, and drop paths so they cannot drift.
    /// </summary>
    private void AdoptImage(string previewUrl, byte[]? bytes, string contentType, string fileName)
    {
        imagePreviewUrl = previewUrl;
        SessionService.SetImage(previewUrl, contentType, fileName, bytes);
        // Fire-and-forget has bitten us before: a transient 5xx silently produced a gallery
        // with no entry. Delegate to UserImageSaveService which sends an Idempotency-Key and
        // surfaces a Retry-button Radzen toast on failure. The _ = drop is intentional here
        // because we don't want to block the upload UI waiting for storage.
        if (_userId is not null && bytes is not null)
            _ = AutoSaveOriginalAsync(bytes, contentType, fileName);
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

    /// <summary>
    /// Saves the just-uploaded original into the user's gallery via <see cref="UserImageSaveService"/>.
    /// On failure the service surfaces a Radzen toast with a Retry button — no silent drops. Made
    /// virtual so derived pages (MemeGeneration, BulkGenerate) can pass extra tags before save.
    /// </summary>
    protected virtual async Task AutoSaveOriginalAsync(byte[] bytes, string contentType, string fileName)
    {
        var savedId = await UserImageSave.SaveOriginalAsync(bytes, contentType, fileName, tags: null);
        if (savedId is not null && _gallery is not null)
            await _gallery.LoadAsync();
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
        await AiSelection.EnsureInitializedAsync(ct);

        var request = new ImageAnalysisRequest
        {
            ImageData = imageData,
            ContentType = contentType,
            FileName = fileName,
            DescriptionLength = descriptionLength,
            Mode = mode,
            ModelId = AiSelection.Get(AiCapability.AnalyzeImage),
            // Never a guess: null degrades to the server's own ImageGen:Provider fallback rather
            // than stamping a provider id this client is not confident is actually configured.
            ImageGenModelId = AiSelection.GetExplicit(AiCapability.GenerateImage),
        };

        if (!AiSelection.GetOption(AiCapability.AnalyzeImage).ExecutesInBrowser)
        {
            return request;
        }

        var imageBytes = Convert.FromBase64String(imageData);

        // Florence-2 is task-token driven (<CAPTION>, <MORE_DETAILED_CAPTION>, <OD>, ...), not an
        // instruction-following model — a free-form sentence puts it out of distribution. Passing
        // null (not a sentence) lets transformers-worker.js apply its own task-token default; do not
        // "helpfully" replace this with prose.
        var outcome = await LocalAi.DescribeImageAsync(
            imageBytes,
            prompt: null,
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
    /// <remarks>
    /// This is a deliberately simple punctuation-and-length heuristic, not a tokenizer: short
    /// filler words that happen to be 3+ letters (e.g. "with", "this", "from") will survive into
    /// the tag list. That is an accepted simplification — a caption model tolerates a stray filler
    /// word — not an oversight. If the heuristic yields nothing at all (terse captions like
    /// "Cat." are common from browser vision models), the trimmed description itself is used as a
    /// single fallback tag so <c>GenerateMemeCaptionAsync</c> is never invoked with zero grounding.
    /// </remarks>
    internal static IReadOnlyList<string> ExtractTags(string description)
    {
        var tags = description
            .Split([' ', ',', '.', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => w.Length >= 3)
            .Select(w => w.ToLowerInvariant())
            .Distinct()
            .Take(10)
            .ToList();

        if (tags.Count > 0) return tags;

        var trimmed = description.Trim();
        return trimmed.Length == 0 ? [] : [trimmed.ToLowerInvariant()];
    }

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
