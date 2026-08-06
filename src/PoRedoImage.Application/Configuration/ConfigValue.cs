using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace PoRedoImage.Application.Configuration;

/// <summary>
/// Trim-safe replacements for <c>ConfigurationBinder.GetValue&lt;T&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// §1 Trimming turns <c>EnableTrimAnalyzer</c> on for every project, and <c>GetValue&lt;T&gt;</c> is
/// annotated <c>[RequiresUnreferencedCode]</c> (IL2026) because it resolves a <c>TypeConverter</c>
/// for <c>T</c> by reflection. For the scalar types this solution actually reads — bool, float,
/// double — the reflective binder buys nothing over a direct <c>TryParse</c>.
/// </para>
/// <para>
/// Two deliberate behaviour choices: parsing is <see cref="CultureInfo.InvariantCulture"/> so a
/// server running under a comma-decimal locale reads <c>"0.6"</c> the same way the appsettings
/// author wrote it, and a malformed value degrades to <paramref name="defaultValue"/> instead of
/// throwing — <c>GetValue&lt;T&gt;</c> throws <c>InvalidOperationException</c>, which on a singleton
/// service that re-reads rotated Key Vault secrets means a request-time crash rather than a
/// startup one.
/// </para>
/// </remarks>
public static class ConfigValue
{
    /// <summary>Reads a boolean flag; missing or unparseable yields <paramref name="defaultValue"/>.</summary>
    public static bool Bool(IConfiguration? configuration, string key, bool defaultValue = false) =>
        bool.TryParse(configuration?[key], out var value) ? value : defaultValue;

    /// <summary>Reads a single-precision value; missing or unparseable yields <paramref name="defaultValue"/>.</summary>
    public static float Float(IConfiguration? configuration, string key, float defaultValue) =>
        float.TryParse(configuration?[key], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : defaultValue;

    /// <summary>Reads an optional double; missing or unparseable yields <c>null</c>.</summary>
    public static double? Double(IConfiguration? configuration, string key) =>
        double.TryParse(configuration?[key], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
