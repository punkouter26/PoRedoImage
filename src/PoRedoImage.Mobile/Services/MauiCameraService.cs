using PoRedoImage.Shared.DTOs;

namespace PoRedoImage.Mobile.Services;

/// <summary>
/// Native MAUI implementation of camera capture and photo gallery picking.
/// </summary>
public class MauiCameraService : ICameraService
{
    private readonly IImageOptimizationService _optimizer;

    public MauiCameraService(IImageOptimizationService optimizer)
    {
        _optimizer = optimizer;
    }

    public bool IsCaptureSupported => MediaPicker.Default.IsCaptureSupported;

    public async Task<ImageCaptureResult?> CapturePhotoAsync(CancellationToken ct = default)
    {
        if (!MediaPicker.Default.IsCaptureSupported)
            return null;

        var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
        if (status != PermissionStatus.Granted)
        {
            status = await Permissions.RequestAsync<Permissions.Camera>();
            if (status != PermissionStatus.Granted)
                return null;
        }

        try
        {
            var photo = await MediaPicker.Default.CapturePhotoAsync(new MediaPickerOptions
            {
                Title = "Capture Image for PoRedo"
            });

            if (photo == null)
                return null;

            await using var stream = await photo.OpenReadAsync();
            var fileName = photo.FileName ?? $"camera_{DateTime.UtcNow:yyyyMMdd_HHmmss}.jpg";
            var contentType = photo.ContentType ?? "image/jpeg";

            return await _optimizer.OptimizeAsync(stream, fileName, contentType, maxDimension: 1280, quality: 85, ct: ct);
        }
        catch (Exception)
        {
            // User cancelled or camera interaction failed
            return null;
        }
    }

    public async Task<ImageCaptureResult?> PickPhotoAsync(CancellationToken ct = default)
    {
        try
        {
            var photos = await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions
            {
                Title = "Select Image for PoRedo"
            });

            var photo = photos?.FirstOrDefault();
            if (photo == null)
                return null;

            await using var stream = await photo.OpenReadAsync();
            var fileName = photo.FileName ?? $"gallery_{DateTime.UtcNow:yyyyMMdd_HHmmss}.jpg";
            var contentType = photo.ContentType ?? "image/jpeg";

            return await _optimizer.OptimizeAsync(stream, fileName, contentType, maxDimension: 1280, quality: 85, ct: ct);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
