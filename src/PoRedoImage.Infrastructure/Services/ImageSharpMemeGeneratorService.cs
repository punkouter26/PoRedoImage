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

    public Task<(byte[] ImageData, string ContentType)>
        GenerateMemeAsync(byte[] sourceImage, string topText, string bottomText, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sourceImage);
        if (sourceImage.Length == 0) throw new ArgumentException("Image data cannot be empty", nameof(sourceImage));

        _logger.LogInformation("Generating meme. Top='{Top}', Bottom='{Bottom}'", topText, bottomText);

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
        var result = outputStream.ToArray();

        _logger.LogInformation("Meme generated. Size={Size} bytes", result.Length);
        return Task.FromResult<(byte[], string)>((result, "image/png"));
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

        float fontSize = maxFontSize;
        while (fontSize > minFontSize)
        {
            var probe = fontFamily.CreateFont(fontSize, FontStyle.Bold);
            var measured = TextMeasurer.MeasureBounds(text, new TextOptions(probe) { WrappingLength = imageWidth - padding * 2 });
            if (measured.Width <= imageWidth - padding * 2) break;
            fontSize -= 2f;
        }

        fontSize = Math.Max(fontSize, minFontSize);
        var font = fontFamily.CreateFont(fontSize, FontStyle.Bold);
        float strokeWidth = Math.Max(fontSize / 8f, 1.5f);
        float yPos = isTop ? padding : imageHeight * 0.65f;

        var textOptions = new RichTextOptions(font)
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Origin = new PointF(imageWidth / 2f, yPos),
            WrappingLength = imageWidth - padding * 2
        };

        ctx.DrawText(textOptions, text, Brushes.Solid(Color.White), Pens.Solid(Color.Black, strokeWidth));
    }
}
