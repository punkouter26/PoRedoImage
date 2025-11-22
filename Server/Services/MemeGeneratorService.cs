using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace Server.Services;

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

    /// <summary>
    /// Adds meme-style caption overlay to an image (white Impact font with black outline)
    /// </summary>
    [SupportedOSPlatform("windows")]
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

            // Draw the original image
            graphics.DrawImage(originalImage, 0, 0, originalImage.Width, originalImage.Height);

            // Add top text if provided
            if (!string.IsNullOrWhiteSpace(topText))
            {
                DrawMemeText(graphics, topText.ToUpperInvariant(), originalImage.Width, originalImage.Height, isTop: true);
            }

            // Add bottom text if provided
            if (!string.IsNullOrWhiteSpace(bottomText))
            {
                DrawMemeText(graphics, bottomText.ToUpperInvariant(), originalImage.Width, originalImage.Height, isTop: false);
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
    private void DrawMemeText(Graphics graphics, string text, int imageWidth, int imageHeight, bool isTop)
    {
        // Calculate max dimensions
        float padding = imageWidth * 0.04f;
        float maxWidth = imageWidth - (padding * 2);
        float maxFontSize = imageHeight / 8f;
        float minFontSize = Math.Max(12f, imageHeight / 40f);
        float currentFontSize = maxFontSize;

        Font font = null;
        
        // Try to find the best font size that fits
        // We prefer the text to fit on one line, but if it's too long, we'll let it wrap
        // by using a rectangle layout. However, we first try to scale it down to fit width
        // if it's reasonably close.
        
        while (currentFontSize >= minFontSize)
        {
            try 
            { 
                font?.Dispose();
                font = new Font("Impact", currentFontSize, FontStyle.Bold, GraphicsUnit.Pixel); 
            }
            catch 
            { 
                font?.Dispose();
                font = new Font("Arial", currentFontSize, FontStyle.Bold, GraphicsUnit.Pixel); 
            }

            var size = graphics.MeasureString(text, font);
            if (size.Width <= maxWidth)
            {
                break; // It fits!
            }
            
            currentFontSize -= 2f;
        }

        // If we hit minFontSize and it still doesn't fit, we'll rely on wrapping in the rectangle
        if (font == null) // Should not happen unless loop doesn't run
        {
             try { font = new Font("Impact", minFontSize, FontStyle.Bold, GraphicsUnit.Pixel); }
             catch { font = new Font("Arial", minFontSize, FontStyle.Bold, GraphicsUnit.Pixel); }
        }

        using (font)
        {
            // Configure text format
            var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = isTop ? StringAlignment.Near : StringAlignment.Far
            };

            // Create brushes and pens
            using var whiteBrush = new SolidBrush(Color.White);
            using var blackPen = new Pen(Color.Black, Math.Max(3, font.Size / 15f))
            {
                LineJoin = LineJoin.Round
            };

            // Calculate text position
            // We give it more vertical space to allow for wrapping if needed
            float yPos = isTop ? imageHeight * 0.02f : imageHeight * 0.75f;
            float height = isTop ? imageHeight * 0.4f : imageHeight * 0.23f; // More space for top, bottom is usually constrained
            
            // Adjust bottom yPos if we need more height for wrapping
            if (!isTop)
            {
                // Measure height with wrapping
                var size = graphics.MeasureString(text, font, (int)maxWidth);
                if (size.Height > height)
                {
                    // Move up to accommodate
                    yPos = imageHeight - size.Height - (imageHeight * 0.02f);
                    height = size.Height + (imageHeight * 0.02f);
                }
            }

            var textRect = new RectangleF(
                padding, 
                yPos,
                maxWidth, 
                height
            );

            // Create path for the text to enable outlining
            using var path = new GraphicsPath();
            path.AddString(
                text,
                font.FontFamily,
                (int)font.Style,
                font.Size, // Use pixel size directly
                textRect,
                format
            );

            // Draw black outline
            graphics.DrawPath(blackPen, path);
            
            // Fill with white
            graphics.FillPath(whiteBrush, path);

            _logger.LogDebug("Drew meme text: '{Text}' at {Position} with size {Size}", text, isTop ? "top" : "bottom", font.Size);
        }
    }
}
