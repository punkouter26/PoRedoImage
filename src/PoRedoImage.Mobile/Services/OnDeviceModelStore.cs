namespace PoRedoImage.Mobile.Services;

/// <summary>
/// Where a side-loaded model is, and whether it is usable.
/// </summary>
/// <param name="IsAvailable">True when every file the runtime needs is present.</param>
/// <param name="Directory">Resolved model directory, or null when nothing was found.</param>
/// <param name="Detail">Human-readable status for Settings and for error messages.</param>
public sealed record OnDeviceModelStatus(bool IsAvailable, string? Directory, string Detail);

/// <summary>
/// Locates side-loaded model weights on the device.
/// </summary>
public interface IOnDeviceModelStore
{
    /// <summary>
    /// Checks the filesystem for <paramref name="model"/>. Cheap enough to call on every page show;
    /// it stats a handful of files rather than reading them.
    /// </summary>
    OnDeviceModelStatus Probe(OnDeviceModel model);

    /// <summary>
    /// The directory the push script targets, shown in Settings so the user can compare it with
    /// what adb reports.
    /// </summary>
    string PreferredRoot { get; }
}

/// <summary>
/// Filesystem-backed <see cref="IOnDeviceModelStore"/>.
/// </summary>
/// <remarks>
/// The preferred root is the app's <em>external</em> files directory rather than
/// <c>FileSystem.AppDataDirectory</c>. That is the whole reason side-loading works: internal
/// storage under <c>/data/data</c> is unreadable to the adb shell user, so a model pushed there
/// would need root. External app-scoped storage is writable by adb and still private to the app.
/// </remarks>
public sealed class OnDeviceModelStore : IOnDeviceModelStore
{
    /// <summary>
    /// Files ONNX Runtime GenAI will not start without. <c>model.onnx.data</c> is deliberately not
    /// in this list — it is external-weights storage whose filename is chosen by whoever exported
    /// the model, so its absence has to surface as a load failure rather than a probe failure.
    /// </summary>
    private static readonly string[] RequiredFiles =
    [
        "genai_config.json",
        "tokenizer.json",
    ];

    public string PreferredRoot => Roots().First();

    public OnDeviceModelStatus Probe(OnDeviceModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        foreach (var root in Roots())
        {
            var dir = Path.Combine(root, model.Id);
            if (!Directory.Exists(dir))
                continue;

            var missing = RequiredFiles.Where(f => !File.Exists(Path.Combine(dir, f))).ToArray();
            if (missing.Length > 0)
            {
                return new OnDeviceModelStatus(
                    false,
                    dir,
                    $"Incomplete transfer — {string.Join(", ", missing)} missing from {dir}.");
            }

            var bytes = TotalBytes(dir);
            return new OnDeviceModelStatus(
                true,
                dir,
                $"Ready · {FormatSize(bytes)} at {dir}");
        }

        return new OnDeviceModelStatus(
            false,
            null,
            $"Not installed. Push it with SCRIPTS/push-mobile-model.ps1 ({FormatSize(model.ApproxBytes)}), " +
            $"which writes to {Path.Combine(PreferredRoot, model.Id)}.");
    }

    /// <summary>
    /// Search order: the adb-writable external directory first, then internal app data so a future
    /// in-app download has somewhere to land that needs no storage permission.
    /// </summary>
    private static IEnumerable<string> Roots()
    {
#if ANDROID
        var external = Android.App.Application.Context?.GetExternalFilesDir(null)?.AbsolutePath;
        if (!string.IsNullOrEmpty(external))
            yield return Path.Combine(external, "models");
#endif
        yield return Path.Combine(FileSystem.Current.AppDataDirectory, "models");
    }

    private static long TotalBytes(string dir)
    {
        try
        {
            return new DirectoryInfo(dir).EnumerateFiles().Sum(f => f.Length);
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):F2} GB",
        >= 1024 * 1024 => $"{bytes / (1024.0 * 1024):F0} MB",
        >= 1024 => $"{bytes / 1024.0:F0} KB",
        _ => $"{bytes} B",
    };
}
