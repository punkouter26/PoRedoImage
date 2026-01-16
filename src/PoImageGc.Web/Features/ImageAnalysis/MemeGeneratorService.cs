using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.Versioning;

namespace PoImageGc.Web.Features.ImageAnalysis;

/// <summary>
/// Interface for meme generation service
/// </summary>
public interface IMemeGeneratorService
{
    byte[] AddCaptionToImage(byte[] imageData, string? topText, string? bottomText);
}

/// <summary>
/// Service for generating meme images with caption overlays
/// </summary>
[SupportedOSPlatform("windows")]
public class MemeGeneratorService : IMemeGeneratorService
{
    private readonly ILogger<MemeGeneratorService> _logger;

    public MemeGeneratorService(ILogger<MemeGeneratorService> logger)
    {
        _logger = logger;
    }

    [SupportedOSPlatform("windows")]
    public byte[] AddCaptionToImage(byte[] imageData, string? topText, string? bottomText)
    {
        ArgumentNullException.ThrowIfNull(imageData);
        if (imageData.Length == 0)
            throw new ArgumentException("Image data cannot be empty", nameof(imageData));

        _logger.LogInformation("Adding meme caption. Top: '{Top}', Bottom: '{Bottom}'", 
            topText ?? "(none)", bottomText ?? "(none)");

        try
        {
            using var ms = new MemoryStream(imageData);
            using var originalImage = Image.FromStream(ms);
            using var bitmap = new Bitmap(originalImage.Width, originalImage.Height);
            using var graphics = Graphics.FromImage(bitmap);

            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.TextRenderingHint = TextRenderingHint.AntiAlias;

            graphics.DrawImage(originalImage, 0, 0, originalImage.Width, originalImage.Height);

            if (!string.IsNullOrWhiteSpace(topText))
            {
                DrawMemeText(graphics, topText.ToUpperInvariant(), originalImage.Width, originalImage.Height, isTop: true);
            }

            if (!string.IsNullOrWhiteSpace(bottomText))
            {
                DrawMemeText(graphics, bottomText.ToUpperInvariant(), originalImage.Width, originalImage.Height, isTop: false);
            }

            using var outputStream = new MemoryStream();
            bitmap.Save(outputStream, ImageFormat.Png);
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

    private static void DrawMemeText(Graphics graphics, string text, int imageWidth, int imageHeight, bool isTop)
    {
        float padding = imageWidth * 0.04f;
        float maxFontSize = imageHeight / 8f;
        float minFontSize = Math.Max(12f, imageHeight / 40f);
        float currentFontSize = maxFontSize;

        using var fontFamily = new FontFamily("Impact");
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = isTop ? StringAlignment.Near : StringAlignment.Far
        };

        while (currentFontSize >= minFontSize)
        {
            using var font = new Font(fontFamily, currentFontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            var textSize = graphics.MeasureString(text, font, imageWidth - (int)(padding * 2));

            if (textSize.Width <= imageWidth - (padding * 2))
                break;

            currentFontSize -= 2;
        }

        using var finalFont = new Font(fontFamily, Math.Max(currentFontSize, minFontSize), FontStyle.Bold, GraphicsUnit.Pixel);
        
        var rect = new RectangleF(
            padding,
            isTop ? padding : imageHeight * 0.65f,
            imageWidth - (padding * 2),
            imageHeight * 0.35f - padding);

        using var outlinePen = new Pen(Color.Black, finalFont.Size / 8f) { LineJoin = LineJoin.Round };
        using var fillBrush = new SolidBrush(Color.White);
        using var path = new GraphicsPath();

        path.AddString(text, fontFamily, (int)FontStyle.Bold, finalFont.Size, rect, format);

        graphics.DrawPath(outlinePen, path);
        graphics.FillPath(fillBrush, path);
    }
}
