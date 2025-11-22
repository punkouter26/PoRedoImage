using System.Text;
using System.Linq;

namespace Server.Services;

/// <summary>
/// Creates narrative-style descriptions from Computer Vision tags when LLM generation is unavailable.
/// </summary>
public interface IImageDescriptionBuilder
{
    /// <summary>
    /// Builds a multi-sentence description using detected tags and confidence.
    /// </summary>
    /// <param name="tags">Ordered list of tags returned by Computer Vision.</param>
    /// <param name="confidenceScore">Optional confidence score (0-1).</param>
    /// <param name="targetLength">Approximate target word count for the generated text.</param>
    /// <returns>Human-readable description.</returns>
    string BuildRichDescription(IReadOnlyList<string> tags, double confidenceScore, int targetLength = 75);
}

/// <summary>
/// Deterministic implementation that converts tag metadata into a richer narrative to avoid single-word fallbacks.
/// </summary>
public class ImageDescriptionBuilder : IImageDescriptionBuilder
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    private static readonly HashSet<string> SubjectTags = new(
        new[] { "person", "people", "man", "woman", "boy", "girl", "child", "adult", "grandparent", "smile", "couple" }, Comparer);

    private static readonly HashSet<string> EnvironmentTags = new(
        new[] { "outdoor", "indoor", "street", "house", "building", "garden", "yard", "room", "kitchen", "living room" }, Comparer);

    private static readonly HashSet<string> NatureTags = new(
        new[] { "tree", "plant", "flower", "grass", "sky", "cloud", "sunlight", "bush", "leaf" }, Comparer);

    private static readonly HashSet<string> ColorAndLightingTags = new(
        new[] { "bright", "colorful", "shadow", "sunny", "daytime", "night", "vibrant", "warm", "cool", "sunset" }, Comparer);

    private static readonly HashSet<string> ActivityTags = new(
        new[] { "walking", "standing", "posing", "holding", "shopping", "talking", "playing" }, Comparer);

    public string BuildRichDescription(IReadOnlyList<string> tags, double confidenceScore, int targetLength = 75)
    {
        if (tags == null || tags.Count == 0)
        {
            return "No visual tags were detected, so a descriptive summary is unavailable.";
        }

        var normalizedTags = tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(Comparer)
            .ToList();

        if (normalizedTags.Count == 0)
        {
            return "No visual tags were detected, so a descriptive summary is unavailable.";
        }

        var subjects = normalizedTags.Where(tag => SubjectTags.Contains(tag)).ToList();
        var environments = normalizedTags.Where(tag => EnvironmentTags.Contains(tag)).ToList();
        var nature = normalizedTags.Where(tag => NatureTags.Contains(tag)).ToList();
        var palette = normalizedTags.Where(tag => ColorAndLightingTags.Contains(tag)).ToList();
        var activities = normalizedTags.Where(tag => ActivityTags.Contains(tag)).ToList();

        var supportingObjects = normalizedTags
            .Except(subjects, Comparer)
            .Except(environments, Comparer)
            .Except(nature, Comparer)
            .Except(palette, Comparer)
            .Except(activities, Comparer)
            .Take(6)
            .ToList();

        var sb = new StringBuilder();
        var environmentPhrase = environments.Any()
            ? $"{FormatList(environments)} setting"
            : "scene";

        var subjectPhrase = subjects.Any()
            ? FormatList(subjects)
            : "various elements";

        var activitySnippet = activities.Any()
            ? $" while {FormatList(activities)}"
            : string.Empty;

        sb.Append($"This {environmentPhrase} centers on {subjectPhrase}{activitySnippet}, captured in a single cohesive moment.");

        if (supportingObjects.Any() || nature.Any())
        {
            var detailParts = new List<string>();
            if (nature.Any())
            {
                detailParts.Add($"natural details such as {FormatList(nature)}");
            }

            if (supportingObjects.Any())
            {
                detailParts.Add($"additional elements like {FormatList(supportingObjects)}");
            }

            sb.Append(' ');
            sb.Append($"The frame also highlights {string.Join(" and ", detailParts)}.");
        }

        if (palette.Any())
        {
            sb.Append(' ');
            sb.Append($"Lighting cues from {FormatList(palette)} create a distinct mood that guides the viewer's attention.");
        }

        if (confidenceScore > 0)
        {
            sb.Append(' ');
            sb.Append($"Analysis confidence is approximately {confidenceScore:P0}, reinforcing the presence of these subjects and surroundings.");
        }

        // Attempt to loosely match requested length by repeating detail sentences with additional context if very short
        var words = sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < targetLength * 0.5)
        {
            sb.Append(' ');
            sb.Append("Overall, the composition feels balanced with foreground subjects grounding the scene while the background context provides depth and atmosphere.");
        }

        return sb.ToString();
    }

    private static string FormatList(IReadOnlyList<string> items)
    {
        if (items.Count == 0)
        {
            return string.Empty;
        }

        if (items.Count == 1)
        {
            return items[0];
        }

        if (items.Count == 2)
        {
            return $"{items[0]} and {items[1]}";
        }

        return string.Join(", ", items.Take(items.Count - 1)) + $", and {items[^1]}";
    }
}
