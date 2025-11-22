using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Server.Services;
using System;
using System.IO;

namespace ImageGc.Tests.Services;

public class MemeGeneratorServiceTests : TestBase
{
    private readonly Mock<ILogger<MemeGeneratorService>> _mockLogger;
    private readonly MemeGeneratorService _service;

    public MemeGeneratorServiceTests()
    {
        _mockLogger = new Mock<ILogger<MemeGeneratorService>>();
        _service = new MemeGeneratorService(_mockLogger.Object);
    }

    [Fact]
    public void AddCaptionToImage_WithValidImage_ReturnsModifiedImage()
    {
        // Arrange - Create a simple test image (1x1 pixel PNG)
        byte[] testImage = CreateTestImageBytes();
        string topText = "TOP TEXT";
        string bottomText = "BOTTOM TEXT";

        // Act
        var result = _service.AddCaptionToImage(testImage, topText, bottomText);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
        Assert.True(result.Length > testImage.Length); // Should be larger due to text overlay
    }

    [Fact]
    public void AddCaptionToImage_WithTopTextOnly_ReturnsModifiedImage()
    {
        // Arrange
        byte[] testImage = CreateTestImageBytes();
        string topText = "ONLY TOP";

        // Act
        var result = _service.AddCaptionToImage(testImage, topText, null);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    [Fact]
    public void AddCaptionToImage_WithBottomTextOnly_ReturnsModifiedImage()
    {
        // Arrange
        byte[] testImage = CreateTestImageBytes();
        string bottomText = "ONLY BOTTOM";

        // Act
        var result = _service.AddCaptionToImage(testImage, null, bottomText);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    [Fact]
    public void AddCaptionToImage_WithNullImage_ThrowsArgumentException()
    {
        // Arrange
        byte[]? nullImage = null;

        // Act & Assert
        Assert.Throws<ArgumentException>(() => 
            _service.AddCaptionToImage(nullImage!, "TOP", "BOTTOM"));
    }

    [Fact]
    public void AddCaptionToImage_WithEmptyImage_ThrowsArgumentException()
    {
        // Arrange
        byte[] emptyImage = Array.Empty<byte>();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => 
            _service.AddCaptionToImage(emptyImage, "TOP", "BOTTOM"));
    }

    [Fact]
    public void AddCaptionToImage_WithNoText_ReturnsModifiedImage()
    {
        // Arrange - Even with no text, should process successfully
        byte[] testImage = CreateTestImageBytes();

        // Act
        var result = _service.AddCaptionToImage(testImage, null, null);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    [Fact]
    public void AddCaptionToImage_WithLongText_TruncatesOrHandlesGracefully()
    {
        // Arrange
        byte[] testImage = CreateTestImageBytes();
        string longText = new string('A', 200); // Very long text

        // Act
        var result = _service.AddCaptionToImage(testImage, longText, longText);

        // Assert - Should not throw, should handle gracefully
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    [Fact]
    public void AddCaptionToImage_WithSpecialCharacters_HandlesCorrectly()
    {
        // Arrange
        byte[] testImage = CreateTestImageBytes();
        string specialText = "HELLO! @#$% & *()";

        // Act
        var result = _service.AddCaptionToImage(testImage, specialText, specialText);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
    }

    [Fact]
    public void AddCaptionToImage_WithInvalidImageData_ThrowsInvalidOperationException()
    {
        // Arrange - Random bytes that aren't a valid image
        byte[] invalidImage = new byte[] { 1, 2, 3, 4, 5 };

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => 
            _service.AddCaptionToImage(invalidImage, "TOP", "BOTTOM"));
    }

    [Fact]
    public void AddCaptionToImage_LogsInformation()
    {
        // Arrange
        byte[] testImage = CreateTestImageBytes();
        string topText = "TOP";
        string bottomText = "BOTTOM";

        // Act
        var result = _service.AddCaptionToImage(testImage, topText, bottomText);

        // Assert - Verify logging occurred (at least once)
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Adding meme caption")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    /// <summary>
    /// Creates a minimal valid PNG image as byte array for testing
    /// </summary>
    private byte[] CreateTestImageBytes()
    {
        // Create a simple 100x100 white PNG image
        using var bitmap = new System.Drawing.Bitmap(100, 100);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.Clear(System.Drawing.Color.White);
        
        using var ms = new MemoryStream();
        bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
    }
}
