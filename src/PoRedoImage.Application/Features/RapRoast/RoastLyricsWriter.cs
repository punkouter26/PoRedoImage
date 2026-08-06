using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Shared.DTOs;

namespace PoRedoImage.Application.Features.RapRoast;

/// <summary>
/// Writes the roast bars from an image description.
/// </summary>
/// <remarks>
/// Backed by <see cref="IChatCompletionService"/> (Azure OpenAI) with a deterministic heuristic
/// fallback, mirroring the Style Director agents — so the feature still works when no chat provider
/// is configured, which is the normal local-development state.
/// </remarks>
public sealed class RoastLyricsWriter(IChatCompletionService chat, ILogger<RoastLyricsWriter> logger)
{
    /// <summary>
    /// The content guardrail, shared by both the normal and softened passes.
    /// </summary>
    /// <remarks>
    /// This is a product decision and a practical one at once. Lyria applies safety filters to every
    /// prompt, so a roast that crosses these lines is refused upstream and the user gets no track —
    /// keeping the jabs on choices rather than characteristics is what makes the feature work at all.
    /// </remarks>
    internal const string Guardrail =
        "Roast ONLY things the subject chose or is doing: outfit, styling, pose, facial expression, "
        + "props, background, and overall vibe. "
        + "NEVER reference or imply race, ethnicity, skin tone, disability, body weight or size, "
        + "age, gender identity, sexual orientation, religion, or medical conditions. "
        + "No slurs, no profanity, no sexual content, no threats. "
        + "Keep it the kind of playful burn friends trade with each other, not cruelty.";

    private const string SystemPrompt =
        "You are a battle-rap ghostwriter writing a short, funny roast verse. "
        + "Reply with ONLY the lyrics — no preamble, no explanation, no markdown fences. "
        + "Structure the output with section tags on their own lines: [Verse] then [Chorus]. "
        + "Write 8 lines under [Verse] and 4 under [Chorus]. Make the lines rhyme and scan to a beat. "
        + Guardrail;

    public async Task<RoastLyrics> WriteAsync(
        string imageDescription,
        IReadOnlyList<string> tags,
        RapStyle style,
        bool softened,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        if (chat.IsConfigured)
        {
            try
            {
                var lyrics = await WriteWithAiAsync(imageDescription, tags, style, softened, ct);
                if (!string.IsNullOrWhiteSpace(lyrics))
                {
                    sw.Stop();
                    return new RoastLyrics(lyrics, softened, sw.ElapsedMilliseconds);
                }

                logger.LogWarning("Roast lyric model returned empty content; falling back to heuristic.");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Roast lyric AI path failed; falling back to heuristic.");
            }
        }

        sw.Stop();
        return new RoastLyrics(Heuristic(imageDescription, tags, softened), softened, sw.ElapsedMilliseconds);
    }

    private async Task<string> WriteWithAiAsync(
        string imageDescription, IReadOnlyList<string> tags, RapStyle style, bool softened, CancellationToken ct)
    {
        // The softened pass is the retry after the music provider declined the first attempt. It is
        // deliberately a *stronger* instruction rather than a different prompt shape, so the output
        // stays structurally identical and the client renders it the same way.
        var toneDirection = softened
            ? "This is a SECOND attempt — the first was rejected as too harsh. Dial the burns "
              + "right down: affectionate teasing only, closer to a friendly nudge than a roast."
            : "Land real punchlines, but keep them good-natured.";

        var user =
            $"Photo description: {imageDescription}\n"
            + $"Detected tags: {string.Join(", ", tags)}\n"
            + $"Track style: {StyleDescription(style)}\n\n"
            + $"{toneDirection}\n"
            + "Write the roast verse and chorus now.";

        var result = await chat.CompleteAsync(SystemPrompt, user, image: null, ct);

        logger.LogInformation(
            "Roast lyrics written by model in {Elapsed}ms. Tokens={Tokens}, Softened={Softened}",
            result.ElapsedMs, result.TokensUsed, softened);

        return Sanitize(result.Content);
    }

    /// <summary>
    /// Deterministic fallback. Structurally identical to the AI output (same section tags) so every
    /// downstream consumer — the music model and the UI — behaves the same either way.
    /// </summary>
    private static string Heuristic(string imageDescription, IReadOnlyList<string> tags, bool softened)
    {
        var subject = tags.FirstOrDefault() ?? "you";
        var second = tags.Skip(1).FirstOrDefault() ?? "that look";
        var edge = softened ? "gentle" : "sharp";

        return $"""
            [Verse]
            Stepped in the frame with a {edge} kind of grin,
            {subject} on display and the camera let you in.
            {second} doing work that the mirror never checked,
            somebody call the stylist, tell 'em come collect.
            You posed like the moment owed you a favour,
            held it for a beat and then held it way later.
            The background saw it all and the background stayed quiet,
            one look at this picture and the whole room went riot.

            [Chorus]
            That's the shot, that's the shot, that's the one you kept,
            out of all of them you took, that's the one you kept.
            No notes, no filter, nothing left to fix,
            {subject} in the frame doing tricks.
            """;
    }

    private static string StyleDescription(RapStyle style) => style switch
    {
        RapStyle.Trap => "modern trap — 808 sub-bass, rolling hi-hats, half-time around 140 BPM",
        RapStyle.OldSchool => "old-school party rap — funk break, horn stabs, around 105 BPM",
        _ => "90s boom-bap — dusty drums, vinyl crackle, around 90 BPM",
    };

    /// <summary>
    /// Strips markdown fences some models add despite the instruction not to. Cheap insurance —
    /// a stray ``` would otherwise be sung as part of the lyrics.
    /// </summary>
    private static string Sanitize(string content)
    {
        var text = content.Trim();

        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline >= 0) text = text[(firstNewline + 1)..];
        }

        if (text.EndsWith("```", StringComparison.Ordinal))
        {
            text = text[..^3];
        }

        return text.Trim();
    }
}

/// <summary>Lyrics plus how they were produced.</summary>
/// <param name="Text">Section-tagged lyrics, ready for the music model.</param>
/// <param name="Softened">True when this is the toned-down retry pass.</param>
/// <param name="ElapsedMs">Wall-clock time to produce them.</param>
public sealed record RoastLyrics(string Text, bool Softened, long ElapsedMs);
