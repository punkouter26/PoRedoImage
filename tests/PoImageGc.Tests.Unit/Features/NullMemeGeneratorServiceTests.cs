using Microsoft.Extensions.Logging;
using Moq;
using PoImageGc.Web.Features.ImageAnalysis;

namespace PoImageGc.Tests.Unit.Features;

/// <summary>
/// Unit tests for NullMemeGeneratorService — Null Object pattern implementation
/// that returns original image data unchanged on non-Windows platforms.
/// </summary>
public class NullMemeGeneratorServiceTests
{
    private readonly NullMemeGeneratorService _service;

    public NullMemeGeneratorServiceTests()
    {
        var loggerMock = new Mock<ILogger<NullMemeGeneratorService>>();
        _service = new NullMemeGeneratorService(loggerMock.Object);
    }

    [Fact]
    public void AddCaptionToImage_ReturnsOriginalImageUnchanged()
    {
        // Arrange
        var imageData = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A };

        // Act
        var result = _service.AddCaptionToImage(imageData, "TOP", "BOTTOM");

        // Assert — Null Object pattern: returns the same byte array reference
        Assert.Same(imageData, result);
    }

    [Fact]
    public void AddCaptionToImage_NullCaptions_ReturnsOriginalImage()
    {
        // Arrange
        var imageData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };

        // Act
        var result = _service.AddCaptionToImage(imageData, null, null);

        // Assert
        Assert.Same(imageData, result);
    }
}
