using Microsoft.Extensions.Logging;
using Moq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using PoRedoImage.Infrastructure.Services;

namespace PoRedoImage.Tests.Unit.Features;

/// <summary>
/// Unit tests for ImageSharpMemeGeneratorService.
/// Uses a programmatically generated minimal PNG so no external assets are required.
/// </summary>
public class MemeGeneratorServiceTests
{
    private readonly Mock<ILogger<ImageSharpMemeGeneratorService>> _loggerMock = new();

    private ImageSharpMemeGeneratorService CreateService() =>
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
    public async Task GenerateMemeAsync_NullImageData_ThrowsArgumentNullException()
    {
        var svc = CreateService();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            svc.GenerateMemeAsync(null!, "TOP", "BOTTOM"));
    }

    [Fact]
    public async Task GenerateMemeAsync_EmptyImageData_ThrowsArgumentException()
    {
        var svc = CreateService();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.GenerateMemeAsync([], "TOP", "BOTTOM"));
    }

    // ─── Output correctness ─────────────────────────────────────────

    [Fact]
    public async Task GenerateMemeAsync_ValidPng_ReturnsNonEmptyBytes()
    {
        var svc = CreateService();
        var (result, _) = await svc.GenerateMemeAsync(CreateMinimalPng(), "TOP TEXT", "BOTTOM TEXT");

        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task GenerateMemeAsync_NullCaptions_ReturnsImageWithoutThrow()
    {
        var svc = CreateService();
        // Null top and bottom text — service should skip drawing and return the image unchanged
        var (result, _) = await svc.GenerateMemeAsync(CreateMinimalPng(), null!, null!);

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task GenerateMemeAsync_EmptyCaptions_ReturnsImageWithoutThrow()
    {
        var svc = CreateService();
        var (result, _) = await svc.GenerateMemeAsync(CreateMinimalPng(), "", "");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task GenerateMemeAsync_ValidPng_OutputIsPng()
    {
        var svc = CreateService();
        var (result, _) = await svc.GenerateMemeAsync(CreateMinimalPng(), "HELLO", "WORLD");

        // PNG magic bytes: 89 50 4E 47
        Assert.Equal(0x89, result[0]);
        Assert.Equal(0x50, result[1]);
        Assert.Equal(0x4E, result[2]);
        Assert.Equal(0x47, result[3]);
    }
}
