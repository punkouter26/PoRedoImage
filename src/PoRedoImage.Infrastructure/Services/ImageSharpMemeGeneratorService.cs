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
        float maxFontSize = Math.Min(imageHeight / 8f, imageWidth / 12f);
        float minFontSize = Math.Max(12f, imageHeight / 40f);
        float availableWidth = imageWidth - padding * 2f;
        float yPos = isTop ? padding : imageHeight * 0.65f;

        MemeTextRenderer.DrawText(
            ctx,
            text,
            new PointF(imageWidth / 2f, yPos),
            availableWidth,
            maxFontSize,
            minFontSize,
            HorizontalAlignment.Center);
    }
}
