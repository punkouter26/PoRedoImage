using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using Microsoft.Extensions.Logging;

namespace Server.Services;

/// <summary>
/// Service for generating meme images with caption overlays
/// </summary>
public class MemeGeneratorService : IMemeGeneratorService
{
    private readonly ILogger<MemeGeneratorService> _logger;

    public MemeGeneratorService(ILogger<MemeGeneratorService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Adds meme-style caption overlay to an image (white Impact font with black outline)
    /// </summary>
    public byte[] AddCaptionToImage(byte[] imageData, string? topText, string? bottomText)
    {
        if (imageData == null || imageData.Length == 0)
            throw new ArgumentException("Image data cannot be null or empty", nameof(imageData));

        _logger.LogInformation("Adding meme caption. Top: '{TopText}', Bottom: '{BottomText}'", 
            topText ?? "(none)", bottomText ?? "(none)");

        try
        {
            using var ms = new MemoryStream(imageData);
            using var originalImage = Image.FromStream(ms);
            using var bitmap = new Bitmap(originalImage.Width, originalImage.Height);
            using var graphics = Graphics.FromImage(bitmap);

            // Set high quality rendering
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.TextRenderingHint = TextRenderingHint.AntiAlias;

            // Draw the original image
            graphics.DrawImage(originalImage, 0, 0, originalImage.Width, originalImage.Height);

            // Calculate font size based on image dimensions (approximately 1/10th of image height)
            float fontSize = Math.Max(20, originalImage.Height / 10f);
            
            // Try to use Impact font, fallback to Arial Bold if not available
            Font font;
            try
            {
                font = new Font("Impact", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            }
            catch
            {
                _logger.LogWarning("Impact font not available, falling back to Arial Bold");
                font = new Font("Arial", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            }

            using (font)
            {
                // Add top text if provided
                if (!string.IsNullOrWhiteSpace(topText))
                {
                    DrawMemeText(graphics, topText.ToUpperInvariant(), font, 
                        originalImage.Width, originalImage.Height, isTop: true);
                }

                // Add bottom text if provided
                if (!string.IsNullOrWhiteSpace(bottomText))
                {
                    DrawMemeText(graphics, bottomText.ToUpperInvariant(), font, 
                        originalImage.Width, originalImage.Height, isTop: false);
                }
            }

            // Save to output stream
            using var outputStream = new MemoryStream();
            bitmap.Save(outputStream, ImageFormat.Png);
            var result = outputStream.ToArray();

            _logger.LogInformation("Meme generation completed. Output size: {Size} bytes", result.Length);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating meme image");
            throw new InvalidOperationException("Failed to generate meme image", ex);
        }
    }

    /// <summary>
    /// Draws text with white fill and black outline (classic meme style)
    /// </summary>
    private void DrawMemeText(Graphics graphics, string text, Font font, 
        int imageWidth, int imageHeight, bool isTop)
    {
        // Configure text format
        var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = isTop ? StringAlignment.Near : StringAlignment.Far
        };

        // Create brushes and pens
        using var whiteBrush = new SolidBrush(Color.White);
        using var blackPen = new Pen(Color.Black, Math.Max(2, font.Size / 20f))
        {
            LineJoin = LineJoin.Round
        };

        // Calculate text position
        var textRect = new RectangleF(
            0, 
            isTop ? imageHeight * 0.05f : imageHeight * 0.75f,
            imageWidth, 
            imageHeight * 0.2f
        );

        // Create path for the text to enable outlining
        using var path = new GraphicsPath();
        path.AddString(
            text,
            font.FontFamily,
            (int)font.Style,
            graphics.DpiY * font.Size / 72,
            textRect,
            format
        );

        // Draw black outline
        graphics.DrawPath(blackPen, path);
        
        // Fill with white
        graphics.FillPath(whiteBrush, path);

        _logger.LogDebug("Drew meme text: '{Text}' at {Position}", text, isTop ? "top" : "bottom");
    }
}
