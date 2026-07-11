using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Interfaces;

namespace PoRedoImage.Application.Agents.StyleDirector;

/// <summary>
/// Agent 2 of the Style Director workflow. Given the Vision Analyst's subject/mood/themes, an LLM
/// proposes 2–3 concrete art-style directions (name, palette, technique, era). Falls back to a
/// curated mood→directions library when no chat provider is configured or the call fails.
/// </summary>
/// <remarks>
/// Idea #1 — Agentic Style Director (step 2 of 4).
/// </remarks>
public sealed class StyleStrategistAgent : IAgent<StyleStrategistInput, StyleStrategistOutput>
{
    private readonly IChatCompletionService _chat;
    private readonly ILogger<StyleStrategistAgent> _logger;

    public string Id => "style-strategist";
    public string DisplayName => "Style Strategist";
    public string IconClass => "bi-palette";

    public StyleStrategistAgent(IChatCompletionService chat, ILogger<StyleStrategistAgent> logger)
    {
        _chat = chat;
        _logger = logger;
    }

    public async Task<AgentStepResult<StyleStrategistOutput>> RunAsync(StyleStrategistInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var sw = Stopwatch.StartNew();

        if (_chat.IsConfigured)
        {
            try
            {
                return await RunWithAiAsync(input, sw, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "StyleStrategist AI path failed; falling back to heuristic.");
            }
        }

        return Heuristic(input, sw);
    }

    private async Task<AgentStepResult<StyleStrategistOutput>> RunWithAiAsync(
        StyleStrategistInput input, Stopwatch sw, CancellationToken ct)
    {
        var v = input.Vision;
        const string system =
            "You are the Style Strategist in an art-direction pipeline. Propose distinct, renderable "
            + "art-style directions. Reply with MINIFIED JSON only — no prose, no markdown code fences.";
        var user =
            $"Subject: {v.Subject}. Mood: {v.Mood}. Themes: {string.Join(", ", v.Themes)}. "
            + "Propose 2-3 distinct art-style directions that suit this. Respond as JSON: "
            + "{\"rationale\":\"one sentence on why these fit\",\"directions\":[{\"name\":\"style name\","
            + "\"palette\":\"colour palette\",\"technique\":\"medium/technique\",\"referenceEra\":\"era or movement\"}]}.";

        var result = await _chat.CompleteAsync(system, user, image: null, ct);
        var el = AgentJson.Parse(result.Content);

        var directions = new List<StyleDirection>();
        if (el.TryGetProperty("directions", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var d in arr.EnumerateArray())
            {
                var name = AgentJson.Str(d, "name");
                if (string.IsNullOrWhiteSpace(name)) continue;
                directions.Add(new StyleDirection(
                    name,
                    AgentJson.Str(d, "palette", "balanced tones"),
                    AgentJson.Str(d, "technique", "clean rendering"),
                    AgentJson.Str(d, "referenceEra", "contemporary")));
            }
        }
        if (directions.Count == 0) throw new FormatException("Strategist returned no usable directions.");
        directions = directions.Take(3).ToList();

        var rationale = AgentJson.Str(el, "rationale",
            $"The {v.Mood} mood suggests {directions.Count} directions: {string.Join(" · ", directions.Select(d => d.Name))}.");

        var output = new StyleStrategistOutput(directions, rationale);
        sw.Stop();

        _logger.LogInformation("StyleStrategist (AI) done in {Elapsed}ms. Directions={Count}, Tokens={Tokens}",
            result.ElapsedMs, directions.Count, result.TokensUsed);

        return new AgentStepResult<StyleStrategistOutput>(output, Reasoning(rationale, result.ElapsedMs, result.TokensUsed));
    }

    private AgentStepResult<StyleStrategistOutput> Heuristic(StyleStrategistInput input, Stopwatch sw)
    {
        var directions = MoodToDirections(input.Vision.Mood).Take(3).ToList();
        if (directions.Count == 0)
        {
            directions.Add(new StyleDirection(
                "Editorial Portrait",
                "neutral film tones, low saturation",
                "soft window light, 50mm lens, shallow depth",
                "modern editorial"));
        }

        var rationale = $"The {input.Vision.Mood} mood suggests {directions.Count} stylistic directions: " +
                        string.Join(" · ", directions.Select(d => d.Name)) + ".";

        var output = new StyleStrategistOutput(directions, rationale);
        sw.Stop();

        _logger.LogInformation("StyleStrategist (heuristic) done in {Elapsed}ms. Directions={Count}",
            sw.ElapsedMilliseconds, directions.Count);

        return new AgentStepResult<StyleStrategistOutput>(output, Reasoning(rationale, sw.ElapsedMilliseconds, tokensUsed: null));
    }

    private AgentReasoningEntry Reasoning(string summary, long elapsedMs, int? tokensUsed) => new(
        AgentId: Id,
        AgentDisplayName: DisplayName,
        IconClass: IconClass,
        Summary: summary,
        ElapsedMs: elapsedMs,
        TokensUsed: tokensUsed,
        Timestamp: DateTimeOffset.UtcNow,
        Activity: null);

    private static IEnumerable<StyleDirection> MoodToDirections(string mood) => mood switch
    {
        "serene" => new[]
        {
            new StyleDirection("Studio Ghibli Watercolor", "soft pastels, sky blue, cream",
                "soft brush, light wash, painterly edges", "1980s anime"),
            new StyleDirection("Golden Hour Editorial", "warm amber, peach, soft tan",
                "rim light, low ISO film grain", "2010s editorial"),
            new StyleDirection("Botanical Illustration", "sage green, cream, muted teal",
                "fine line, hand-drawn, ink cross-hatch", "1900s naturalist"),
        },
        "moody" => new[]
        {
            new StyleDirection("Cyberpunk Neon", "deep teal, magenta, electric violet",
                "high contrast, neon rim light, atmospheric haze", "1980s retrofuturism"),
            new StyleDirection("Noir Film Still", "monochrome, deep blacks, silver highlights",
                "hard side light, deep shadows, 35mm grain", "1940s noir"),
            new StyleDirection("Vaporwave Synthwave", "hot pink, cyan, deep purple",
                "soft glow, scan lines, VHS bleed", "1980s synthwave"),
        },
        "candid" => new[]
        {
            new StyleDirection("Lifestyle Documentary", "natural skin tones, warm whites",
                "available light, photojournalistic, candid framing", "modern editorial"),
            new StyleDirection("Polaroid Pop", "saturated primaries, slightly faded",
                "square crop, instant film, soft flash", "1990s instant film"),
            new StyleDirection("Illustrated Storybook", "warm pastels, dusty rose, cream",
                "soft brush, gentle line work, child-book charm", "modern storybook"),
        },
        "playful" => new[]
        {
            new StyleDirection("Pixar Pop", "saturated primary, soft yellow",
                "3D render, subsurface skin, soft global illumination", "modern 3D"),
            new StyleDirection("Comic Book Halftone", "bold primaries, Ben-Day dots",
                "inked outlines, halftone shading, dynamic composition", "1960s comic"),
            new StyleDirection("Risograph Print", "fluorescent pink, teal, mustard",
                "misregistered layers, grain, hand-pulled print", "1990s indie zine"),
        },
        "indulgent" => new[]
        {
            new StyleDirection("Dutch Still-Life", "deep umber, gold, ivory",
                "chiaroscuro, oil paint, fine detail, museum lighting", "1600s baroque"),
            new StyleDirection("Food Magazine Cover", "clean white, accent orange, sage",
                "studio strobe, 100mm macro, 45-degree angle", "modern editorial"),
            new StyleDirection("Vintage Cookbook", "muted terracotta, cream, olive",
                "watercolor wash, hand-lettered, distressed paper", "1950s cookbook"),
        },
        _ => new[]
        {
            new StyleDirection("Editorial Portrait", "neutral film tones, low saturation",
                "soft window light, 50mm lens, shallow depth", "modern editorial"),
            new StyleDirection("Pop Art Treatment", "saturated primaries, hard outlines",
                "screen print, flat color, comic-book inking", "1960s pop art"),
        }
    };
}
