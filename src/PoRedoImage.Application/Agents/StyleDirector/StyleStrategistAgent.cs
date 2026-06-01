using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Interfaces;

namespace PoRedoImage.Application.Agents.StyleDirector;

/// <summary>
/// Agent 2 of the Style Director workflow. Picks 2–3 art-style directions that
/// match the mood inferred by the Vision Analyst. Heuristic-only — the goal is
/// to give the user concrete choices to approve, not to invent styles from scratch.
/// </summary>
/// <remarks>
/// Idea #1 — Agentic Style Director (step 2 of 4).
/// </remarks>
public sealed class StyleStrategistAgent : IAgent<StyleStrategistInput, StyleStrategistOutput>
{
    private readonly ILogger<StyleStrategistAgent> _logger;

    public string Id => "style-strategist";
    public string DisplayName => "Style Strategist";
    public string IconClass => "bi-palette";

    public StyleStrategistAgent(ILogger<StyleStrategistAgent> logger) => _logger = logger;

    public Task<AgentStepResult<StyleStrategistOutput>> RunAsync(StyleStrategistInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var sw = Stopwatch.StartNew();

        // A tiny library of "vibes" → style directions. Each direction is a
        // self-contained, renderable Imagen 3 direction — names + palette + technique.
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

        var reasoning = new AgentReasoningEntry(
            AgentId: Id,
            AgentDisplayName: DisplayName,
            IconClass: IconClass,
            Summary: rationale,
            ElapsedMs: (long)sw.ElapsedMilliseconds,
            TokensUsed: null,
            Timestamp: DateTimeOffset.UtcNow,
            Activity: null);

        _logger.LogInformation("StyleStrategist done in {Elapsed}ms. Directions={Count}",
            reasoning.ElapsedMs, directions.Count);

        return Task.FromResult(new AgentStepResult<StyleStrategistOutput>(output, reasoning));
    }

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
