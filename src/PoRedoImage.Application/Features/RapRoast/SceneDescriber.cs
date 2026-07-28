using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Interfaces;

namespace PoRedoImage.Application.Features.RapRoast;

/// <summary>
/// Produces the detailed scene description the roast is written from.
/// </summary>
/// <remarks>
/// Azure Computer Vision alone is not enough here. Its Caption feature is region-limited, and when
/// it is unavailable <c>AzureVisionService</c> synthesises a description by joining the top eight
/// tags — "A photo showing person, clothing, food, man, indoor, fast food, wall, meal". That is a
/// keyword list, not a scene, and a lyric writer given keywords can only produce generic bars.
/// <para>
/// So when a vision-capable chat model is configured this asks it for the specifics a roast
/// actually needs — what someone is wearing, how they are standing, what is behind them, what is
/// slightly off about the whole arrangement — and falls back to the tag-derived description only
/// when no such model exists.
/// </para>
/// </remarks>
public sealed class SceneDescriber(IChatCompletionService chat, ILogger<SceneDescriber> logger)
{
    private const string SystemPrompt =
        "You are a scene describer feeding a comedy writer. Describe ONLY what is visibly in the "
        + "image, in 4-6 sentences of concrete detail. Cover, in this order: what the person is "
        + "wearing (specific garments, colours, fit, condition); their pose and what their hands "
        + "are doing; their facial expression; the setting and what is on the walls or surfaces "
        + "behind them; any objects, food, or props and their exact state; and finally the single "
        + "most incongruous or funny detail in the frame. "
        + "Be specific and literal — name things. Do NOT describe or infer race, ethnicity, skin "
        + "tone, body size or weight, age, disability, or attractiveness. Do not speculate about "
        + "who the person is. Do not write jokes; just report what is there.";

    /// <summary>
    /// Returns a rich description, or <paramref name="fallbackDescription"/> when no vision-capable
    /// model is available.
    /// </summary>
    public async Task<SceneDescription> DescribeAsync(
        byte[] image,
        string fallbackDescription,
        IReadOnlyList<string> tags,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (!chat.IsConfigured)
        {
            logger.LogInformation("No vision chat model configured — using the tag-derived description.");
            return new SceneDescription(fallbackDescription, Detailed: false, 0);
        }

        var sw = Stopwatch.StartNew();

        try
        {
            // The tags come from the vision backend and are cheap corroboration — they help the
            // model anchor on things it might otherwise gloss over, like the type of venue.
            var user = tags.Count > 0
                ? $"Detected labels (corroboration only, do not just repeat them): {string.Join(", ", tags)}.\n\nDescribe the image."
                : "Describe the image.";

            var result = await chat.CompleteAsync(SystemPrompt, user, image, ct);
            sw.Stop();

            var text = result.Content.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                logger.LogWarning("Vision model returned an empty description; using the tag-derived one.");
                return new SceneDescription(fallbackDescription, Detailed: false, sw.ElapsedMilliseconds);
            }

            logger.LogInformation(
                "Scene described by vision model in {Elapsed}ms. Chars={Chars}, Tokens={Tokens}",
                result.ElapsedMs, text.Length, result.TokensUsed);

            return new SceneDescription(text, Detailed: true, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            // A vision failure must not lose the whole roast — the tag-derived description still
            // produces bars, just blander ones.
            sw.Stop();
            logger.LogWarning(ex, "Vision description failed; falling back to the tag-derived description.");
            return new SceneDescription(fallbackDescription, Detailed: false, sw.ElapsedMilliseconds);
        }
    }
}

/// <summary>A scene description and whether it came from a vision model or the tag fallback.</summary>
/// <param name="Text">The description handed to the lyric writer.</param>
/// <param name="Detailed">True when a vision model produced it.</param>
/// <param name="ElapsedMs">Wall-clock time taken.</param>
public sealed record SceneDescription(string Text, bool Detailed, long ElapsedMs);
