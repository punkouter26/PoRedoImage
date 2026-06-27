using Microsoft.AspNetCore.Components.Forms;

namespace PoRedoImage.Web.Components.Shared;

public static class ImageLoadHelper
{
    private const int MaxFileSize = 20 * 1024 * 1024;

    public sealed record LoadResult(string PreviewUrl, byte[] Bytes, string ContentType);

    /// <summary>
    /// Validates and reads an uploaded browser file. Returns a <see cref="LoadResult"/> on success
    /// or an error message string on failure.
    /// </summary>
    public static async Task<(LoadResult? Result, string? Error)> LoadAsync(IBrowserFile file)
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

            return (new LoadResult($"data:{contentType};base64,{Convert.ToBase64String(bytes)}", bytes, contentType), null);
        }
        catch (Exception ex)
        {
            return (null, $"Failed to load image: {ex.Message}");
        }
    }
}
