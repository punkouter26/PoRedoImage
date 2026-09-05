namespace PoRedoImage.Web.Features.Diagnostics;

/// <summary>
/// Classifies request paths that serve build-output assets rather than application endpoints, so
/// request logging can drop them to Verbose.
/// </summary>
/// <remarks>
/// A Blazor WebAssembly boot fetches the runtime, every referenced assembly, the Radzen bundle and
/// the scoped-CSS bundle — roughly 200 requests. Logged at Information (the configured minimum),
/// that produced 26,267 entries and 12.5 MB of log file in one day of local use, and the same
/// volume as billable Application Insights ingestion in production. None of those lines has ever
/// been used to diagnose anything: a genuinely interesting asset request is one that 4xx/5xx'd,
/// and the caller keeps those at Warning/Error regardless of path.
/// </remarks>
public static class StaticAssetPaths
{
    private static readonly string[] AssetPrefixes =
    [
        "/_framework",   // Blazor runtime, assemblies, boot manifest
        "/_content",     // RCL static web assets (Radzen)
        "/lib",          // vendored css/fonts
        "/css",
        "/js",
    ];

    private static readonly string[] AssetSuffixes =
    [
        ".wasm", ".dll", ".pdb", ".dat", ".blat",
        ".js", ".css", ".map",
        ".woff", ".woff2", ".ttf", ".eot",
        ".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp", ".ico",
    ];

    /// <summary>
    /// True when <paramref name="path"/> is a static build asset rather than an application endpoint.
    /// </summary>
    public static bool IsStaticAsset(PathString path)
    {
        if (!path.HasValue)
            return false;

        var value = path.Value!;

        foreach (var prefix in AssetPrefixes)
        {
            if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (var suffix in AssetSuffixes)
        {
            if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
