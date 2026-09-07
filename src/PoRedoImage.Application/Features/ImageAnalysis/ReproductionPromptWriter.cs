using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Interfaces;

namespace PoRedoImage.Application.Features.ImageAnalysis;

/// <summary>
/// Writes the prompt the image generator reproduces a photograph from.
/// </summary>
/// <remarks>
/// Image regeneration is deliberately text-to-image: the source pixels are never sent to the
/// generator, so the prompt is the <em>only</em> thing carrying the photo across. That makes the
/// prompt the whole product, and the pipeline that used to produce it could not carry a photograph.
/// <para>
/// It ran Azure Computer Vision → <c>EnhanceDescriptionAsync</c>. Computer Vision's Caption feature
/// is region-unavailable here, so <c>AzureVisionService</c> synthesises its description by joining
/// the top tags — "A photo showing person, table, food, indoor" — and the enhancement step then
/// rewrote that keyword list into a vivid but entirely invented scene under a 40–120 word budget
/// tuned for creative prompts. Nothing in that chain ever looked at the pose, the clothing, the
/// lighting, or the framing, so the generator had no way to land near the original.
/// </para>
/// <para>
/// This class replaces both steps with a single vision-language pass that reads the actual pixels
/// and emits a dense, ordered inventory of what is in the frame. Same shape as
/// <c>RapRoast.SceneDescriber</c>, and for the same reason — a tag list is not a description.
/// </para>
/// </remarks>
public sealed class ReproductionPromptWriter(
    IChatCompletionService chat,
    ILogger<ReproductionPromptWriter> logger)
{
    /// <summary>
    /// Ordered because generators weight the front of a prompt most heavily, so the clauses that
    /// decide whether the output is recognisably the same photo have to come first. The ban on
    /// interpretation is load-bearing: given any latitude a chat model writes atmosphere ("a quiet
    /// moment of contemplation"), and atmosphere renders as a different picture.
    /// </summary>
    private const string SystemPrompt =
        "You write prompts for a text-to-image model that must REPRODUCE a photograph it will never "
        + "see. Your prompt is the only thing it receives, so every visual fact you omit is a fact it "
        + "will invent.\n"
        + "Reply with the prompt itself and nothing else — no preamble, no explanation, no markdown, "
        + "no headings, no numbering.\n"
        + "Write comma-separated visual clauses, not sentences, in this order:\n"
        + "1. medium and shot type (photograph, film still, illustration; candid, portrait, close-up, wide shot)\n"
        + "2. the main subject: how many people, what each is doing, their pose, and where they are looking\n"
        + "3. what you would need to redraw them: hair colour, length and style, facial hair, eyewear, "
        + "headwear, and facial expression\n"
        + "4. clothing, garment by garment, with colour, material and fit\n"
        + "5. every significant object, with colour, material, state, and position relative to the subject\n"
        + "6. the setting and background, near to far\n"
        + "7. lighting: direction, hardness, colour temperature, and where highlights and shadows fall\n"
        + "8. the dominant colour palette, named concretely\n"
        + "9. camera angle, height, distance, lens character and depth of field\n"
        + "10. framing: what sits left, centre and right, and what the frame edges cut off\n"
        + "Rules. Describe only what is visible — never invent, never guess at what is out of frame, "
        + "never add a style the photograph does not already have. Be concrete: \"round gold "
        + "wire-frame glasses\" beats \"glasses\", \"mustard yellow\" beats \"yellowish\". Do not "
        + "name or infer race, ethnicity, skin tone, age, body size, or disability. Do not "
        + "editorialise, do not interpret mood, do not tell a story.";

    /// <summary>
    /// Reads the image and returns a reproduction-grade prompt, or a null
    /// <see cref="ReproductionPrompt.Text"/> plus a user-facing reason when no vision model could
    /// produce one — in which case the caller falls back to the tag-derived enhancement path.
    /// </summary>
    /// <param name="targetLength">
    /// The UI's detail preset (200/350/500), used here as an actual word budget rather than the
    /// quarter-size "clause budget" the creative enhancement path applies. The reasoning behind that
    /// smaller budget — that generators taper off and render narrative connective tissue — is about
    /// prose. These are dense clauses with no connective tissue, and for reproduction every extra
    /// clause is another fact the generator does not have to invent.
    /// </param>
    public async Task<ReproductionPrompt> WriteAsync(
        byte[] image,
        string visionDescription,
        IReadOnlyList<string> tags,
        int targetLength,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(tags);

        if (!chat.IsConfigured)
        {
            logger.LogInformation("No vision model configured; cannot write a reproduction prompt.");
            return new ReproductionPrompt(
                null, 0, 0,
                "No vision model is configured, so the new image was drawn from image labels rather "
                + "than from a detailed read of your photo. Set OpenAI:Endpoint and OpenAI:Key to "
                + "have the photo described properly.");
        }

        var budget = Math.Clamp(targetLength, 120, 400);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await chat.CompleteAsync(SystemPrompt, BuildUserPrompt(visionDescription, tags, budget), image, ct);
            sw.Stop();

            var text = result.Content.Trim();

            if (string.IsNullOrWhiteSpace(text))
            {
                logger.LogWarning("Vision model returned an empty reproduction prompt.");
                return new ReproductionPrompt(
                    null, result.TokensUsed, sw.ElapsedMilliseconds,
                    "The vision model returned nothing usable, so the new image was drawn from image "
                    + "labels rather than from a detailed read of your photo. Trying again usually fixes it.");
            }

            logger.LogInformation(
                "Reproduction prompt written in {Elapsed}ms. Words={Words}, Tokens={Tokens}",
                sw.ElapsedMilliseconds, CountWords(text), result.TokensUsed);

            return new ReproductionPrompt(text, result.TokensUsed, sw.ElapsedMilliseconds, FallbackReason: null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            logger.LogWarning(ex, "Reproduction prompt generation failed.");
            return new ReproductionPrompt(null, 0, sw.ElapsedMilliseconds, DescribeFailure(ex));
        }
    }

    private static string BuildUserPrompt(string visionDescription, IReadOnlyList<string> tags, int budget)
    {
        var parts = new List<string>
        {
            $"Use at most {budget} words. Spend them on visual facts, not on grammar.",
        };

        // Corroboration only, and labelled as such. Both of these are usually tag-derived — handing
        // them over unqualified invites the model to pad the prompt back out with the same keyword
        // list this class exists to replace.
        if (tags.Count > 0)
            parts.Add($"Detected labels, for corroboration only — do not simply repeat them: {string.Join(", ", tags)}.");

        if (!string.IsNullOrWhiteSpace(visionDescription))
            parts.Add($"A coarse automatic caption, which may be wrong — trust your own reading of the image over it: \"{visionDescription}\".");

        parts.Add("Study the image and write the reproduction prompt.");
        return string.Join("\n\n", parts);
    }

    /// <summary>
    /// Turns a failed vision call into something the user can act on: a throttled call succeeds on
    /// retry, a misconfigured or filtered one never will.
    /// </summary>
    /// <remarks>
    /// Deliberately a near-twin of the same method in <c>RapRoast.SceneDescriber</c>. The two slices
    /// own their own user-facing copy — the consequence clause is what the reader actually needs and
    /// it differs per feature ("the bars are working from image labels" vs. what follows here).
    /// </remarks>
    private static string DescribeFailure(Exception ex)
    {
        const string consequence =
            "so the new image was drawn from image labels rather than from a detailed read of your photo";

        var message = ex.Message;

        if (message.Contains("429", StringComparison.Ordinal)
            || message.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
            || message.Contains("rate_limit", StringComparison.OrdinalIgnoreCase))
        {
            return $"The vision model was rate-limited, {consequence}. Wait a moment and try again.";
        }

        if (message.Contains("content_filter", StringComparison.OrdinalIgnoreCase)
            || message.Contains("content filter", StringComparison.OrdinalIgnoreCase))
        {
            return $"The vision model declined to describe this image, {consequence}.";
        }

        if (message.Contains("401", StringComparison.Ordinal)
            || message.Contains("403", StringComparison.Ordinal)
            || message.Contains("DeploymentNotFound", StringComparison.OrdinalIgnoreCase))
        {
            return $"The vision model rejected the request (credentials or deployment name), {consequence}. "
                + "Check OpenAI:Key and OpenAI:ChatCompletionsDeployment.";
        }

        return $"The vision model call failed, {consequence}. Trying again usually fixes it.";
    }

    private static int CountWords(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
}

/// <summary>The prompt the generator will draw from, plus how it was produced.</summary>
/// <param name="Text">
/// The reproduction prompt, or null when no vision model could produce one and the caller must fall
/// back to the tag-derived enhancement path.
/// </param>
/// <param name="TokensUsed">Tokens billed for the vision pass.</param>
/// <param name="ElapsedMs">Wall-clock time taken.</param>
/// <param name="FallbackReason">
/// User-facing explanation of why <paramref name="Text"/> is null, or null when it is not.
/// </param>
public sealed record ReproductionPrompt(string? Text, int TokensUsed, long ElapsedMs, string? FallbackReason);
