using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Interfaces;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PoRedoImage.Infrastructure.Services;

/// <summary>
/// Cross-platform meme image generator using SixLabors.ImageSharp.
/// Adapter pattern (GoF): adapts SixLabors API to IMemeGeneratorService interface.
/// </summary>
public sealed class ImageSharpMemeGeneratorService : IMemeGeneratorService
{
    private readonly ILogger<ImageSharpMemeGeneratorService> _logger;

    public ImageSharpMemeGeneratorService(ILogger<ImageSharpMemeGeneratorService> logger) => _logger = logger;

    public async Task<(byte[] ImageData, string ContentType)>
        GenerateMemeAsync(byte[] sourceImage, string topText, string bottomText, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sourceImage);
        if (sourceImage.Length == 0) throw new ArgumentException("Image data cannot be empty", nameof(sourceImage));

        _logger.LogInformation("Generating meme. Top='{Top}', Bottom='{Bottom}'", topText, bottomText);

        var result = await Task.Run(() =>
        {
            using var image = Image.Load<Rgba32>(sourceImage);
            image.Mutate(ctx =>
            {
                if (!string.IsNullOrWhiteSpace(topText))
                    DrawMemeText(ctx, topText.ToUpperInvariant(), image.Width, image.Height, isTop: true);
                if (!string.IsNullOrWhiteSpace(bottomText))
                    DrawMemeText(ctx, bottomText.ToUpperInvariant(), image.Width, image.Height, isTop: false);
            });
            using var outputStream = new MemoryStream();
            image.Save(outputStream, new PngEncoder());
            return outputStream.ToArray();
        }, ct);

        _logger.LogInformation("Meme generated. Size={Size} bytes", result.Length);
        return (result, "image/png");
    }

    private static void DrawMemeText(IImageProcessingContext ctx, string text, int imageWidth, int imageHeight, bool isTop)
    {
        float padding = imageWidth * 0.04f;
        float maxFontSize = imageHeight / 8f;
        float minFontSize = Math.Max(12f, imageHeight / 40f);

        if (!SystemFonts.TryGet("Impact", out var fontFamily) &&
            !SystemFonts.TryGet("Liberation Sans", out fontFamily) &&
            !SystemFonts.TryGet("DejaVu Sans", out fontFamily) &&
            !SystemFonts.TryGet("Arial", out fontFamily) &&
            !SystemFonts.TryGet("Helvetica", out fontFamily))
        {
            fontFamily = SystemFonts.Families.First();
        }

        // Start with a conservative max so even worst-case long captions still
        // shrink to a size that fits within the photo. Long captions are common
        // (e.g. "WHEN YOU TRY TO ADULT / BUT YOU'RE STILL A KID AT HEART").
        maxFontSize = Math.Min(maxFontSize, imageWidth / 12f);

        // Available width accounts for the stroke that is added at render time.
        // Without this, MeasureBounds under-reports the actual rendered width
        // and text overflows the photo.
        var fontSize = maxFontSize;
        Font font;
        float strokeWidth;
        float availableWidth = imageWidth - padding * 2f;
        // Iteratively shrink the font until the measured (stroke-aware) width fits.
        while (fontSize > minFontSize)
        {
            font = fontFamily.CreateFont(fontSize, FontStyle.Bold);
            strokeWidth = Math.Max(fontSize / 8f, 1.5f);
            // The stroke paints half-width on each side of the glyph outline,
            // so the rendered width grows by ~2 * strokeWidth.
            var probeOptions = new TextOptions(font)
            {
                WrappingLength = availableWidth,
                WordBreaking = WordBreaking.BreakWord
            };
            var measured = TextMeasurer.MeasureBounds(text, probeOptions);
            if (measured.Width + (strokeWidth * 2f) <= availableWidth) break;
            // Decrement faster to converge on a fitting size for very long text.
            fontSize -= Math.Max(2f, fontSize * 0.08f);
        }

        fontSize = Math.Max(fontSize, minFontSize);
        font = fontFamily.CreateFont(fontSize, FontStyle.Bold);
        strokeWidth = Math.Max(fontSize / 8f, 1.5f);
        float yPos = isTop ? padding : imageHeight * 0.65f;

        var textOptions = new RichTextOptions(font)
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Origin = new PointF(imageWidth / 2f, yPos),
            WrappingLength = availableWidth,
            WordBreaking = WordBreaking.BreakWord
        };

        ctx.DrawText(textOptions, text, Brushes.Solid(Color.White), Pens.Solid(Color.Black, strokeWidth));
    }
}
