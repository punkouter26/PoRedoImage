using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Entities;
using PoRedoImage.Domain.Interfaces;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PoRedoImage.Infrastructure.Services;

/// <summary>
/// Default <see cref="IMemeTemplateService"/> implementation. Ships a curated catalog of
/// 20 classic meme formats. Renders text into pre-defined zones using ImageSharp — no
/// network calls, no AI cost; pure local image manipulation.
/// </summary>
/// <remarks>
/// Idea #17 — Meme Template Library. Each template encodes text-zone coordinates as
/// normalized 0..1 ratios so the same layout works on any input photo dimensions.
/// </remarks>
public sealed class MemeTemplateService : IMemeTemplateService
{
    private readonly ILogger<MemeTemplateService> _logger;
    private readonly IReadOnlyList<MemeTemplate> _templates;

    public MemeTemplateService(ILogger<MemeTemplateService> logger)
    {
        _logger = logger;
        _templates = BuildCatalog();
        _logger.LogInformation("Meme template library loaded. Templates={Count}", _templates.Count);
    }

    public IReadOnlyList<MemeTemplate> GetTemplates() => _templates;

    public MemeTemplate? GetById(string id) =>
        _templates.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

    public async Task<(byte[] ImageData, string ContentType)> RenderAsync(
        byte[] sourceImage,
        MemeTemplate template,
        IReadOnlyList<string> zoneTexts,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sourceImage);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(zoneTexts);

        if (sourceImage.Length == 0) throw new ArgumentException("Image data cannot be empty", nameof(sourceImage));
        if (zoneTexts.Count < template.RequiredZoneCount)
            throw new ArgumentException(
                $"Template '{template.Id}' requires at least {template.RequiredZoneCount} text zone(s); {zoneTexts.Count} provided.",
                nameof(zoneTexts));

        _logger.LogInformation("Rendering meme template {Template} with {Zones} zones",
            template.Id, zoneTexts.Count);

        var result = await Task.Run(() =>
        {
            using var image = Image.Load<Rgba32>(sourceImage);
            image.Mutate(ctx =>
            {
                for (var i = 0; i < template.Zones.Count && i < zoneTexts.Count; i++)
                {
                    var text = zoneTexts[i];
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    DrawZoneText(ctx, template.Zones[i], text.ToUpperInvariant(), image.Width, image.Height);
                }
            });
            using var ms = new MemoryStream();
            image.Save(ms, new PngEncoder());
            return ms.ToArray();
        }, ct);

        _logger.LogInformation("Meme template rendered. Size={Size} bytes", result.Length);
        return (result, "image/png");
    }

    private static void DrawZoneText(IImageProcessingContext ctx, MemeTextZone zone, string text, int width, int height)
    {
        if (!SystemFonts.TryGet("Impact", out var fontFamily) &&
            !SystemFonts.TryGet("Liberation Sans", out fontFamily) &&
            !SystemFonts.TryGet("DejaVu Sans", out fontFamily) &&
            !SystemFonts.TryGet("Arial", out fontFamily) &&
            !SystemFonts.TryGet("Helvetica", out fontFamily))
        {
            fontFamily = SystemFonts.Families.First();
        }

        var maxFontSize = (float)(height * zone.FontSizeRatio);
        var minFontSize = Math.Max(12f, height / 40f);
        var maxWidth = (float)(width * zone.MaxWidthRatio);

        // Iteratively shrink the font until the stroke-aware measured width fits.
        // TextMeasurer.MeasureBounds does not include the rendered stroke, so we
        // add 2 * strokeWidth when comparing. WordBreaking.Normal enforces a hard
        // wrap so long captions split across lines instead of overflowing.
        var fontSize = maxFontSize;
        Font font;
        float strokeWidth;
        while (fontSize > minFontSize)
        {
            font = fontFamily.CreateFont(fontSize, FontStyle.Bold);
            strokeWidth = Math.Max(fontSize / 8f, 1.5f);
            var probeOptions = new TextOptions(font)
            {
                WrappingLength = maxWidth,
                WordBreaking = WordBreaking.BreakWord
            };
            var measured = TextMeasurer.MeasureBounds(text, probeOptions);
            if (measured.Width + (strokeWidth * 2f) <= maxWidth) break;
            fontSize -= Math.Max(2f, fontSize * 0.08f);
        }
        fontSize = Math.Max(fontSize, minFontSize);

        font = fontFamily.CreateFont(fontSize, FontStyle.Bold);
        strokeWidth = Math.Max(fontSize / 8f, 1.5f);

        var alignment = zone.Alignment.ToLowerInvariant() switch
        {
            "left" => HorizontalAlignment.Left,
            "right" => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Center
        };

        var textOptions = new RichTextOptions(font)
        {
            HorizontalAlignment = alignment,
            VerticalAlignment = VerticalAlignment.Top,
            Origin = new PointF((float)(width * zone.X), (float)(height * zone.Y)),
            WrappingLength = maxWidth,
            WordBreaking = WordBreaking.BreakWord
        };

        ctx.DrawText(textOptions, text, Brushes.Solid(Color.White), Pens.Solid(Color.Black, strokeWidth));
    }

    /// <summary>
    /// Curated catalog of 20 meme templates. Coordinates are normalized 0..1 so any photo
    /// (portrait, landscape, square) maps to the same template without distortion.
    /// </summary>
    private static IReadOnlyList<MemeTemplate> BuildCatalog() => new MemeTemplate[]
    {
        // ── Classic ────────────────────────────────────────────────
        new("impact-top-bottom", "Classic Top/Bottom", "The original. Top caption + bottom caption.", "classic", 2, new[]
        {
            new MemeTextZone("Top",    0.5, 0.05, 0.92, 0.085, "center"),
            new MemeTextZone("Bottom", 0.5, 0.83, 0.92, 0.085, "center"),
        }),
        new("drake", "Drake Hotline Bling", "Rejecting (top) vs. approving (bottom) — two side-by-side captions.", "classic", 2, new[]
        {
            new MemeTextZone("Rejecting", 0.75, 0.30, 0.45, 0.075, "center"),
            new MemeTextZone("Approving", 0.75, 0.78, 0.45, 0.075, "center"),
        }),
        new("distracted-bf", "Distracted Boyfriend", "Three labels: subject, temptation, current love.", "classic", 3, new[]
        {
            new MemeTextZone("Boyfriend", 0.30, 0.10, 0.28, 0.060, "center"),
            new MemeTextZone("Other",     0.72, 0.10, 0.28, 0.060, "center"),
            new MemeTextZone("Girlfriend", 0.50, 0.92, 0.94, 0.060, "center"),
        }),
        new("expanding-brain", "Expanding Brain", "Four escalating takes — top to bottom.", "classic", 4, new[]
        {
            new MemeTextZone("Level 1", 0.5, 0.10, 0.94, 0.080, "center"),
            new MemeTextZone("Level 2", 0.5, 0.35, 0.94, 0.080, "center"),
            new MemeTextZone("Level 3", 0.5, 0.60, 0.94, 0.080, "center"),
            new MemeTextZone("Level 4", 0.5, 0.85, 0.94, 0.080, "center"),
        }),
        new("two-buttons", "Two Buttons", "A choice between two equally stressful options.", "classic", 2, new[]
        {
            new MemeTextZone("Option 1", 0.30, 0.20, 0.40, 0.075, "center"),
            new MemeTextZone("Option 2", 0.70, 0.20, 0.40, 0.075, "center"),
        }),

        // ── Reaction ──────────────────────────────────────────────
        new("change-my-mind", "Change My Mind", "Bold claim on a sign — viewers try to argue.", "reaction", 1, new[]
        {
            new MemeTextZone("Claim", 0.5, 0.78, 0.85, 0.090, "center"),
        }),
        new("uno-draw-25", "UNO Draw 25", "Two captions: the setup and the brutal punchline.", "reaction", 2, new[]
        {
            new MemeTextZone("Setup",    0.5, 0.12, 0.92, 0.075, "center"),
            new MemeTextZone("Punchline",0.5, 0.85, 0.92, 0.075, "center"),
        }),
        new("always-has-been", "Always Has Been", "Astronaut: 'Oh no' → 'Always has been'.", "reaction", 2, new[]
        {
            new MemeTextZone("Astronaut 1", 0.55, 0.30, 0.50, 0.075, "center"),
            new MemeTextZone("Astronaut 2", 0.55, 0.62, 0.50, 0.075, "center"),
        }),
        new("this-is-fine", "This Is Fine", "Top denial, bottom escalating chaos.", "reaction", 2, new[]
        {
            new MemeTextZone("Denial", 0.5, 0.10, 0.92, 0.085, "center"),
            new MemeTextZone("Reality",0.5, 0.85, 0.92, 0.085, "center"),
        }),
        new("surprised-pikachu", "Surprised Pikachu", "Top: obvious bad idea. Bottom: shocked face.", "reaction", 2, new[]
        {
            new MemeTextZone("Bad Idea", 0.5, 0.10, 0.92, 0.080, "center"),
            new MemeTextZone("Reaction", 0.5, 0.85, 0.92, 0.080, "center"),
        }),

        // ── Office / corporate ────────────────────────────────────
        new("boardroom-meeting", "Boardroom Meeting", "Suggestion (top) and the rejected alternative (bottom).", "office", 2, new[]
        {
            new MemeTextZone("Suggestion", 0.5, 0.12, 0.92, 0.080, "center"),
            new MemeTextZone("Alternative",0.5, 0.85, 0.92, 0.080, "center"),
        }),
        new("galaxy-brain", "Galaxy Brain", "Four increasingly smug takes.", "office", 4, new[]
        {
            new MemeTextZone("Basic Take",   0.5, 0.10, 0.94, 0.070, "center"),
            new MemeTextZone("Better Take",  0.5, 0.35, 0.94, 0.070, "center"),
            new MemeTextZone("Galactic Take",0.5, 0.60, 0.94, 0.070, "center"),
            new MemeTextZone("Multiverse",   0.5, 0.85, 0.94, 0.070, "center"),
        }),
        new("meeting", "Could Have Been An Email", "Two captions — meeting vs. the email that would have replaced it.", "office", 2, new[]
        {
            new MemeTextZone("Meeting", 0.5, 0.10, 0.92, 0.080, "center"),
            new MemeTextZone("Email",   0.5, 0.85, 0.92, 0.080, "center"),
        }),

        // ── Wholesome / sentiment ─────────────────────────────────
        new("wholesome-2k", "Wholesome 2,000 Upvotes", "A kind, sincere message.", "wholesome", 1, new[]
        {
            new MemeTextZone("Message", 0.5, 0.45, 0.90, 0.090, "center"),
        }),
        new("you-get-a", "You Get A", "Big, all-caps excitement.", "wholesome", 1, new[]
        {
            new MemeTextZone("Caption", 0.5, 0.10, 0.92, 0.110, "center"),
        }),

        // ── Experimental ─────────────────────────────────────────
        new("three-panel", "Three-Panel Story", "Setup → development → payoff.", "experimental", 3, new[]
        {
            new MemeTextZone("Setup",   0.5, 0.05, 0.92, 0.070, "center"),
            new MemeTextZone("Middle",  0.5, 0.45, 0.92, 0.070, "center"),
            new MemeTextZone("Payoff",  0.5, 0.85, 0.92, 0.070, "center"),
        }),
        new("left-right-quote", "Left/Right Quote", "Speaker on the left, quote on the right.", "experimental", 2, new[]
        {
            new MemeTextZone("Speaker", 0.25, 0.50, 0.40, 0.080, "center"),
            new MemeTextZone("Quote",   0.75, 0.50, 0.40, 0.080, "center"),
        }),
        new("book-cover", "Book Cover", "Title (top), author-style subtitle (middle), tagline (bottom).", "experimental", 3, new[]
        {
            new MemeTextZone("Title",    0.5, 0.20, 0.92, 0.090, "center"),
            new MemeTextZone("Subtitle", 0.5, 0.50, 0.92, 0.060, "center"),
            new MemeTextZone("Tagline",  0.5, 0.85, 0.92, 0.060, "center"),
        }),
        new("newspaper", "Newspaper Headline", "Big headline (top), byline (bottom).", "experimental", 2, new[]
        {
            new MemeTextZone("Headline", 0.5, 0.10, 0.92, 0.080, "center"),
            new MemeTextZone("Byline",   0.5, 0.92, 0.92, 0.060, "center"),
        }),
        new("caption-only", "Caption Only", "Single centered line — minimal.", "experimental", 1, new[]
        {
            new MemeTextZone("Caption", 0.5, 0.45, 0.90, 0.100, "center"),
        }),
    };
}
