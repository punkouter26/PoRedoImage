using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Platform;
using PoRedoImage.Shared.DTOs;

namespace PoRedoImage.Mobile.Services;

/// <summary>
/// Downscales large camera photos to mobile-optimized AI dimensions to ensure sub-second uploads.
/// </summary>
public class ImageOptimizationService : IImageOptimizationService
{
    public async Task<ImageCaptureResult> OptimizeAsync(
        Stream imageStream,
        string fileName,
        string contentType,
        int maxDimension = 1280,
        int quality = 85,
        CancellationToken ct = default)
    {
        using var memoryStream = new MemoryStream();
        await imageStream.CopyToAsync(memoryStream, ct);
        var bytes = memoryStream.ToArray();

        return await OptimizeAsync(bytes, fileName, contentType, maxDimension, quality, ct);
    }

    public async Task<ImageCaptureResult> OptimizeAsync(
        byte[] rawBytes,
        string fileName,
        string contentType,
        int maxDimension = 1280,
        int quality = 85,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var inStream = new MemoryStream(rawBytes);
                var platformImage = PlatformImage.FromStream(inStream);

                if (platformImage == null)
                {
                    // Fallback to raw bytes if decoding fails
                    var rawBase64 = Convert.ToBase64String(rawBytes);
                    return new ImageCaptureResult(
                        fileName,
                        contentType,
                        rawBytes,
                        rawBase64,
                        rawBytes.Length);
                }

                var originalWidth = (int)platformImage.Width;
                var originalHeight = (int)platformImage.Height;

                // Check if resizing is necessary
                if (originalWidth <= maxDimension && originalHeight <= maxDimension)
                {
                    var base64 = Convert.ToBase64String(rawBytes);
                    return new ImageCaptureResult(
                        fileName,
                        contentType,
                        rawBytes,
                        base64,
                        rawBytes.Length,
                        originalWidth,
                        originalHeight);
                }

                // Calculate aspect ratio preserving dimensions
                double ratio = (double)originalWidth / originalHeight;
                int targetWidth, targetHeight;

                if (originalWidth > originalHeight)
                {
                    targetWidth = maxDimension;
                    targetHeight = (int)Math.Round(maxDimension / ratio);
                }
                else
                {
                    targetHeight = maxDimension;
                    targetWidth = (int)Math.Round(maxDimension * ratio);
                }

                using var downscaled = platformImage.Downsize(targetWidth, targetHeight, true);
                using var outStream = new MemoryStream();
                downscaled.Save(outStream, ImageFormat.Jpeg, (float)(quality / 100.0));
                var optimizedBytes = outStream.ToArray();
                var optimizedBase64 = Convert.ToBase64String(optimizedBytes);

                return new ImageCaptureResult(
                    Path.ChangeExtension(fileName, ".jpg"),
                    "image/jpeg",
                    optimizedBytes,
                    optimizedBase64,
                    optimizedBytes.Length,
                    targetWidth,
                    targetHeight);
            }
            catch
            {
                // Resilient fallback to original bytes
                var base64 = Convert.ToBase64String(rawBytes);
                return new ImageCaptureResult(
                    fileName,
                    contentType,
                    rawBytes,
                    base64,
                    rawBytes.Length);
            }
        }, ct);
    }
}

