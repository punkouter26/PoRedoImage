using System.Text.Json;

namespace PoRedoImage.Application.Agents.StyleDirector;

/// <summary>
/// Lenient JSON extraction helpers shared by the Style Director agents. LLMs frequently wrap JSON in
/// prose or markdown code fences, so <see cref="Parse"/> pulls the outermost <c>{ … }</c> object and
/// the typed accessors tolerate missing/mistyped fields (returning the caller's fallback) rather than
/// throwing — an agent that can't parse the model output degrades to its heuristic path.
/// </summary>
internal static class AgentJson
{
    /// <summary>Extracts and parses the first top-level JSON object from a model response.</summary>
    public static JsonElement Parse(string content)
    {
        var s = content.Trim();
        var start = s.IndexOf('{');
        var end = s.LastIndexOf('}');
        if (start < 0 || end <= start)
            throw new FormatException("No JSON object found in model response.");
        s = s[start..(end + 1)];
        using var doc = JsonDocument.Parse(s);
        return doc.RootElement.Clone();
    }

    public static string Str(JsonElement e, string prop, string fallback = "")
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? (v.GetString() is { Length: > 0 } s ? s : fallback)
            : fallback;

    public static int Int(JsonElement e, string prop, int fallback)
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)
            ? i
            : fallback;

    public static List<string> StrArray(JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()!)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList()
            : [];
}
