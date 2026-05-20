using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using PoRedoImage.Shared.DTOs;
using PoRedoImage.Web.Components.Shared;
using Radzen;
using System.Security.Claims;

namespace PoRedoImage.Web.Components.Pages;

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
}
