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
/// Code-behind for <c>Gallery.razor</c>. The markup file keeps its directives and template;
/// all logic lives here so neither half has to be read through the other.
/// </summary>
public partial class Gallery
{
    private bool _loading = true;
    private string? _loadError;
    private List<UserImageDto> _images = new();
    private string _searchQuery = string.Empty;
    private UserImageKind? _selectedKind;
    private HashSet<string> _selectedIds = new();
    private string? _deletingId;
    private bool _batchWorking;

    protected override async Task OnInitializedAsync()
    {
        await LoadGalleryAsync();
    }

    private async Task LoadGalleryAsync()
    {
        _loading = true;
        _loadError = null;
        try
        {
            var res = await Http.GetFromJsonAsync<List<UserImageDto>>("/api/user-images");
            _images = res?.OrderByDescending(x => x.CreatedAt).ToList() ?? new List<UserImageDto>();
            _selectedIds.Clear();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load gallery");
            _loadError = "Could not connect to the storage service. Ensure backend services are running.";
        }
        finally
        {
            _loading = false;
        }
    }

    private List<UserImageDto> FilteredImages
    {
        get
        {
            var q = _images.AsEnumerable();
            if (_selectedKind.HasValue)
            {
                q = q.Where(x => x.Kind == _selectedKind.Value);
            }
            if (!string.IsNullOrWhiteSpace(_searchQuery))
            {
                var query = _searchQuery.Trim().ToLowerInvariant();
                q = q.Where(x =>
                    (x.FileName?.ToLowerInvariant().Contains(query) ?? false) ||
                    (x.Tags != null && x.Tags.Any(t => t.ToLowerInvariant().Contains(query))));
            }
            return q.ToList();
        }
    }

    private int CountFor(UserImageKind kind) => _images.Count(x => x.Kind == kind);

    private void ToggleSelect(string id)
    {
        if (!_selectedIds.Add(id))
            _selectedIds.Remove(id);
    }

    private void ToggleSelectAll()
    {
        var currentFiltered = FilteredImages;
        if (_selectedIds.Count == currentFiltered.Count)
        {
            _selectedIds.Clear();
        }
        else
        {
            _selectedIds = new HashSet<string>(currentFiltered.Select(x => x.Id));
        }
    }

    private async Task OpenLightboxAsync(UserImageDto item)
    {
        var galleryItem = new MyImagesGallery.GalleryItem(
            item.Id,
            item.FileName,
            item.ContentType,
            item.Kind,
            item.CreatedAt,
            item.SizeBytes,
            item.ImageUrl,
            item.Tags ?? Array.Empty<string>());

        var choice = await DialogService.OpenAsync<GalleryLightbox>(
            item.FileName,
            new Dictionary<string, object>
            {
                ["Item"] = galleryItem,
                ["IconClass"] = KindIcon(item.Kind)
            },
            new DialogOptions
            {
                CloseDialogOnOverlayClick = true,
                ShowClose = true,
                CssClass = "gallery-lightbox"
            });

        if (choice is GalleryLightbox.LightboxResult.Use)
        {
            UseAsInput(item);
        }
        else if (choice is GalleryLightbox.LightboxResult.Download)
        {
            await JS.InvokeAsync<bool>("downloadImage", item.ImageUrl, item.FileName);
        }
    }

    private void UseAsInput(UserImageDto item)
    {
        SessionService.SetImage(item.ImageUrl, item.ContentType, item.FileName);
        NotificationService.Notify(NotificationSeverity.Info, "Session Image Set",
            $"Loaded {item.FileName} as active session image.", duration: 3000);
        Nav.NavigateTo("/");
    }

    private async Task CopyToClipboardAsync(string url)
    {
        var outcome = await JS.InvokeAsync<string>("poUx.copyImageToClipboard", url);
        if (outcome == "copied")
            NotificationService.Notify(NotificationSeverity.Success, "Copied", "Image copied to clipboard.", duration: 2500);
        else
            NotificationService.Notify(NotificationSeverity.Warning, "Unavailable", "Clipboard copy failed or unsupported in this browser.", duration: 3000);
    }

    private async Task DeleteSingleAsync(UserImageDto item)
    {
        _deletingId = item.Id;
        try
        {
            var res = await Http.DeleteAsync($"/api/user-images/{item.Id}");
            if (res.IsSuccessStatusCode)
            {
                _images.RemoveAll(x => x.Id == item.Id);
                _selectedIds.Remove(item.Id);
                NotificationService.Notify(NotificationSeverity.Success, "Deleted", $"Removed {item.FileName}", duration: 2500);
            }
            else
            {
                NotificationService.Notify(NotificationSeverity.Error, "Delete failed", "Could not delete image from gallery.", duration: 3500);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to delete image {Id}", item.Id);
        }
        finally
        {
            _deletingId = null;
        }
    }

    private async Task DeleteSelectedAsync()
    {
        if (_selectedIds.Count == 0 || _batchWorking) return;
        _batchWorking = true;
        int deleted = 0;
        var toDelete = _selectedIds.ToList();
        foreach (var id in toDelete)
        {
            try
            {
                var res = await Http.DeleteAsync($"/api/user-images/{id}");
                if (res.IsSuccessStatusCode)
                {
                    _images.RemoveAll(x => x.Id == id);
                    _selectedIds.Remove(id);
                    deleted++;
                }
            }
            catch { /* proceed with batch */ }
        }
        _batchWorking = false;
        NotificationService.Notify(NotificationSeverity.Info, "Batch Delete", $"Deleted {deleted} image(s).", duration: 3000);
    }

    private async Task DownloadSelectedZipAsync()
    {
        if (_selectedIds.Count == 0 || _batchWorking) return;
        _batchWorking = true;
        try
        {
            var selectedItems = _images.Where(x => _selectedIds.Contains(x.Id)).ToList();
            var files = selectedItems.Select(x => new { name = x.FileName, url = x.ImageUrl }).ToArray();
            var count = await JS.InvokeAsync<int>("poUx.downloadZip", files, $"poredoimage-gallery-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip");
            NotificationService.Notify(NotificationSeverity.Success, "ZIP Ready", $"Archived {count} image(s). Check downloads.", duration: 3500);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to build ZIP");
            NotificationService.Notify(NotificationSeverity.Error, "ZIP Failed", "Could not package images into ZIP.", duration: 3500);
        }
        finally
        {
            _batchWorking = false;
        }
    }

    private static string KindIcon(UserImageKind kind) => kind switch
    {
        UserImageKind.Original => "bi-camera",
        UserImageKind.Regeneration => "bi-palette2",
        UserImageKind.Meme => "bi-chat-square-text",
        UserImageKind.BulkVariation => "bi-grid-3x3",
        _ => "bi-image"
    };

    private static string KindBadgeClass(UserImageKind kind) => kind switch
    {
        UserImageKind.Original => "bg-primary",
        UserImageKind.Regeneration => "bg-success",
        UserImageKind.Meme => "bg-info text-dark",
        UserImageKind.BulkVariation => "bg-warning text-dark",
        _ => "bg-secondary"
    };

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{(bytes / 1024.0):F1} KB";
        return $"{(bytes / (1024.0 * 1024.0)):F1} MB";
    }
}
