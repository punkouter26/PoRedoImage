using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PoImageGc.Web.Features.ImageAnalysis;

/// <summary>
/// Interface for meme generation service
/// </summary>
public interface IMemeGeneratorService
{
    byte[] AddCaptionToImage(byte[] imageData, string? topText, string? bottomText);
}

/// <summary>
/// Cross-platform meme image generator using SixLabors.ImageSharp.
/// Replaces the Windows-only System.Drawing implementation so the service
/// works identically on the Linux Azure App Service host.
/// </summary>
public class MemeGeneratorService : IMemeGeneratorService
{
    private readonly ILogger<MemeGeneratorService> _logger;

    public MemeGeneratorService(ILogger<MemeGeneratorService> logger)
    {
        _logger = logger;
    }

    public byte[] AddCaptionToImage(byte[] imageData, string? topText, string? bottomText)
    {
        ArgumentNullException.ThrowIfNull(imageData);
        if (imageData.Length == 0)
            throw new ArgumentException("Image data cannot be empty", nameof(imageData));

        _logger.LogInformation("Adding meme caption. Top: '{Top}', Bottom: '{Bottom}'",
            topText ?? "(none)", bottomText ?? "(none)");

        try
        {
            using var image = Image.Load<Rgba32>(imageData);

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

            _logger.LogInformation("Meme generated. Output size: {Size} bytes", result.Length);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating meme image");
            throw new InvalidOperationException("Failed to generate meme image", ex);
        }
    }

    private static void DrawMemeText(
        IImageProcessingContext ctx, string text, int imageWidth, int imageHeight, bool isTop)
    {
        float padding = imageWidth * 0.04f;
        float maxFontSize = imageHeight / 8f;
        float minFontSize = Math.Max(12f, imageHeight / 40f);

        // Prefer Impact (classic meme font); fall back to first available system font
        if (!SystemFonts.TryGet("Impact", out var fontFamily))
        {
            var families = SystemFonts.Families.ToList();
            if (families.Count == 0)
                throw new InvalidOperationException("No system fonts are available");
            fontFamily = families[0];
        }

        // Scale font down until the text fits within the image width
        float fontSize = maxFontSize;
        while (fontSize > minFontSize)
        {
            var probe = fontFamily.CreateFont(fontSize, FontStyle.Bold);
            var measured = TextMeasurer.MeasureBounds(text, new TextOptions(probe)
            {
                WrappingLength = imageWidth - padding * 2
            });

            if (measured.Width <= imageWidth - padding * 2)
                break;

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

        // White fill with black outline — classic meme style
        var outlinePen = Pens.Solid(Color.Black, strokeWidth);
        var fillBrush = Brushes.Solid(Color.White);

        ctx.DrawText(new DrawingOptions(), textOptions, text, fillBrush, outlinePen);
    }
}
