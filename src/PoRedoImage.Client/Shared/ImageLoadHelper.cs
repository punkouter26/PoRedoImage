using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace PoRedoImage.Client.Shared;

public static class ImageLoadHelper
{
    private const int MaxFileSize = 20 * 1024 * 1024;

    /// <summary>
    /// Longest edge, in pixels, that any image is sent upstream at.
    /// </summary>
    /// <remarks>
    /// 1568px is the point past which the models in this pipeline stop resolving more detail —
    /// Azure Computer Vision, the chat deployment's vision path and Gemini's reference-image input
    /// all work at roughly one to two megapixels. A 12MP phone photo is about six times that, and
    /// it is carried as base64 (a further +33%) through analysis, through generation, and — before
    /// the batch endpoint existed — through ten separate bulk uploads. Nothing downstream was ever
    /// going to look at those pixels.
    /// </remarks>
    public const int MaxUploadEdge = 1568;

    public sealed record LoadResult(string PreviewUrl, byte[] Bytes, string ContentType);

    /// <summary>
    /// Validates and reads an uploaded browser file. Returns a <see cref="LoadResult"/> on success
    /// or an error message string on failure.
    /// </summary>
    /// <param name="js">
    /// When supplied, the image is downscaled to <see cref="MaxUploadEdge"/> before it becomes the
    /// session image. Optional so a caller with no JS runtime to hand still works — the downscale
    /// is an optimisation, never a precondition.
    /// </param>
    public static async Task<(LoadResult? Result, string? Error)> LoadAsync(
        IBrowserFile file, IJSRuntime? js = null)
    {
        try
        {
            var fileType = Path.GetExtension(file.Name).ToLower();
            if (fileType != ".jpg" && fileType != ".jpeg" && fileType != ".png")
                return (null, "Only JPG and PNG files are supported.");

            if (file.Size > MaxFileSize)
                return (null, $"File size exceeds the maximum allowed (20 MB). Current: {Math.Round(file.Size / 1024.0 / 1024.0, 2)} MB");

            using var ms = new MemoryStream();
            await using var stream = file.OpenReadStream(MaxFileSize);
            await stream.CopyToAsync(ms);
            var bytes = ms.ToArray();

            var contentType = file.ContentType;
            if (string.IsNullOrEmpty(contentType))
                contentType = fileType is ".jpg" or ".jpeg" ? "image/jpeg" : "image/png";

            var dataUrl = $"data:{contentType};base64,{Convert.ToBase64String(bytes)}";
            return (await ShrinkAsync(dataUrl, contentType, bytes, js), null);
        }
        catch (Exception ex)
        {
            return (null, $"Failed to load image: {ex.Message}");
        }
    }

    /// <summary>
    /// Downscales a data URL to <see cref="MaxUploadEdge"/> and returns the result as a
    /// <see cref="LoadResult"/>. Shared by the file, paste and drop intake paths so a pasted
    /// screenshot is treated exactly like an uploaded photo.
    /// </summary>
    public static async Task<LoadResult> ShrinkAsync(
        string dataUrl, string contentType, byte[] originalBytes, IJSRuntime? js)
    {
        if (js is null) return new LoadResult(dataUrl, originalBytes, contentType);

        try
        {
            var shrunk = await js.InvokeAsync<string>("imageProcessing.downscale", dataUrl, MaxUploadEdge);
            if (string.IsNullOrEmpty(shrunk) || ReferenceEquals(shrunk, dataUrl) || shrunk == dataUrl)
                return new LoadResult(dataUrl, originalBytes, contentType);

            var comma = shrunk.IndexOf(";base64,", StringComparison.Ordinal);
            if (comma < 0) return new LoadResult(dataUrl, originalBytes, contentType);

            var newContentType = shrunk[5..comma];
            var newBytes = Convert.FromBase64String(shrunk[(comma + 8)..]);

            return new LoadResult(shrunk, newBytes, newContentType);
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or FormatException)
        {
            // The original is always a valid answer. A browser that cannot run the canvas path
            // (or a malformed round-trip) costs bandwidth, not correctness.
            return new LoadResult(dataUrl, originalBytes, contentType);
        }
    }
}
