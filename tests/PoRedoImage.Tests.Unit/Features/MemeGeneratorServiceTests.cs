using Microsoft.Extensions.Logging;
using Moq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using PoRedoImage.Web.Features.ImageAnalysis;

namespace PoRedoImage.Tests.Unit.Features;

/// <summary>
/// Unit tests for MemeGeneratorService.
/// Uses a programmatically generated minimal PNG so no external assets are required.
/// </summary>
public class MemeGeneratorServiceTests
{
    private readonly Mock<ILogger<MemeGeneratorService>> _loggerMock = new();

    private MemeGeneratorService CreateService() =>
        new(_loggerMock.Object);

    /// <summary>Generates a valid 1×1 transparent PNG byte array at test time.</summary>
    private static byte[] CreateMinimalPng()
    {
        using var image = new Image<Rgba32>(1, 1);
        using var ms = new MemoryStream();
        image.Save(ms, new PngEncoder());
        return ms.ToArray();
    }

    // ─── Guard clauses ──────────────────────────────────────────────

    [Fact]
    public void AddCaptionToImage_NullImageData_ThrowsArgumentNullException()
    {
        var svc = CreateService();
        Assert.Throws<ArgumentNullException>(() =>
            svc.AddCaptionToImage(null!, "TOP", "BOTTOM"));
    }

    [Fact]
    public void AddCaptionToImage_EmptyImageData_ThrowsArgumentException()
    {
        var svc = CreateService();
        Assert.Throws<ArgumentException>(() =>
            svc.AddCaptionToImage([], "TOP", "BOTTOM"));
    }

    // ─── Output correctness ─────────────────────────────────────────

    [Fact]
    public void AddCaptionToImage_ValidPng_ReturnsNonEmptyBytes()
    {
        var svc = CreateService();
        var result = svc.AddCaptionToImage(CreateMinimalPng(), "TOP TEXT", "BOTTOM TEXT");

        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public void AddCaptionToImage_NullCaptions_ReturnsImageWithoutThrow()
    {
        var svc = CreateService();
        // Null top and bottom text — service should skip drawing and return the image unchanged
        var result = svc.AddCaptionToImage(CreateMinimalPng(), null, null);

        Assert.NotEmpty(result);
    }

    [Fact]
    public void AddCaptionToImage_EmptyCaptions_ReturnsImageWithoutThrow()
    {
        var svc = CreateService();
        var result = svc.AddCaptionToImage(CreateMinimalPng(), "", "");

        Assert.NotEmpty(result);
    }

    [Fact]
    public void AddCaptionToImage_ValidPng_OutputIsPng()
    {
        var svc = CreateService();
        var result = svc.AddCaptionToImage(CreateMinimalPng(), "HELLO", "WORLD");

        // PNG magic bytes: 89 50 4E 47
        Assert.Equal(0x89, result[0]);
        Assert.Equal(0x50, result[1]);
        Assert.Equal(0x4E, result[2]);
        Assert.Equal(0x47, result[3]);
    }
}
