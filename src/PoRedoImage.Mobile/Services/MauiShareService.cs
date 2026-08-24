namespace PoRedoImage.Mobile.Services;

/// <summary>
/// Native MAUI implementation of sharing and saving to device filesystem.
/// </summary>
public class MauiShareService : IShareService
{
    public async Task ShareImageAsync(byte[] imageBytes, string fileName, string title = "PoRedo Image")
    {
        var tempPath = Path.Combine(FileSystem.CacheDirectory, fileName);
        await File.WriteAllBytesAsync(tempPath, imageBytes);

        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = title,
            File = new ShareFile(tempPath)
        });
    }

    public async Task ShareTextAsync(string text, string title = "PoRedo Roast")
    {
        await Share.Default.RequestAsync(new ShareTextRequest
        {
            Title = title,
            Text = text
        });
    }

    public async Task<string?> SaveToDeviceAsync(byte[] imageBytes, string fileName)
    {
        try
        {
            var folder = FileSystem.AppDataDirectory;
            var targetPath = Path.Combine(folder, fileName);
            await File.WriteAllBytesAsync(targetPath, imageBytes);
            return targetPath;
        }
        catch
        {
            return null;
        }
    }
}

