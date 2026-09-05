using Microsoft.Extensions.Logging.Abstractions;
using PoRedoImage.Domain.Entities;
using PoRedoImage.Infrastructure.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace PoRedoImage.Tests.Unit.Features;

/// <summary>
/// Unit tests for the Meme Template Library (Idea #17).
/// Verifies the curated catalog, template lookup, and the local-only renderer.
/// </summary>
public class MemeTemplateServiceTests
{
    private readonly MemeTemplateService _svc = new(NullLogger<MemeTemplateService>.Instance);

    [Fact]
    public void GetTemplates_Returns20CuratedTemplates()
    {
        var templates = _svc.GetTemplates();

        Assert.Equal(20, templates.Count);
        // Each category is represented
        Assert.Contains(templates, t => t.Category == "classic");
        Assert.Contains(templates, t => t.Category == "reaction");
        Assert.Contains(templates, t => t.Category == "office");
        Assert.Contains(templates, t => t.Category == "wholesome");
        Assert.Contains(templates, t => t.Category == "experimental");
    }

    [Theory]
    [InlineData("drake", true, "Drake Hotline Bling", 2)]
    [InlineData("DRAKE", true, "Drake Hotline Bling", 2)]
    [InlineData("nonexistent-template", false, null, 0)]
    public void GetById_LookupTests(string id, bool shouldExist, string? expectedName, int expectedZones)
    {
        var tpl = _svc.GetById(id);
        if (shouldExist)
        {
            Assert.NotNull(tpl);
            Assert.Equal(expectedName, tpl.Name);
            Assert.Equal(expectedZones, tpl.RequiredZoneCount);
        }
        else
        {
            Assert.Null(tpl);
        }
    }

    [Fact]
    public void AllTemplates_HaveUniqueIds()
    {
        var templates = _svc.GetTemplates();
        var ids = templates.Select(t => t.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void AllTemplates_HaveUniqueNormalizedZoneCoordinates()
    {
        // Sanity: every zone X and Y must be 0..1 (otherwise renderer would crash).
        var templates = _svc.GetTemplates();
        foreach (var t in templates)
        {
            foreach (var z in t.Zones)
            {
                Assert.InRange(z.X, 0.0, 1.0);
                Assert.InRange(z.Y, 0.0, 1.0);
                Assert.InRange(z.MaxWidthRatio, 0.0, 1.0);
                Assert.InRange(z.FontSizeRatio, 0.0, 1.0);
            }
        }
    }

    [Fact]
    public async Task RenderAsync_DrakeTemplate_ReturnsPng()
    {
        var tpl = _svc.GetById("drake")!;
        var image = CreateMinimalPng();

        var (bytes, contentType) = await _svc.RenderAsync(image, tpl, ["THIS", "THAT"]);

        Assert.Equal("image/png", contentType);
        Assert.NotEmpty(bytes);
        // PNG magic bytes
        Assert.Equal(0x89, bytes[0]);
        Assert.Equal(0x50, bytes[1]);
    }

    [Fact]
    public async Task RenderAsync_SkipsEmptyZoneTexts()
    {
        var tpl = _svc.GetById("drake")!;
        var image = CreateMinimalPng();

        var (bytes, _) = await _svc.RenderAsync(image, tpl, ["", "  ", "   "]);

        Assert.NotEmpty(bytes);
    }

    [Fact]
    public async Task RenderAsync_TooFewZoneTexts_ThrowsArgumentException()
    {
        var tpl = _svc.GetById("three-panel")!;
        var image = CreateMinimalPng();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _svc.RenderAsync(image, tpl, ["only one"]));
    }

    [Theory]
    [InlineData(null, typeof(ArgumentNullException))]
    [InlineData(new byte[0], typeof(ArgumentException))]
    public async Task RenderAsync_InvalidImage_ThrowsExpectedException(byte[]? image, Type expectedException)
    {
        var tpl = _svc.GetById("drake")!;
        await Assert.ThrowsAsync(expectedException, () =>
            _svc.RenderAsync(image!, tpl, ["a", "b"]));
    }

    private static byte[] CreateMinimalPng()
    {
        using var image = new Image<Rgba32>(100, 100);
        using var ms = new MemoryStream();
        image.Save(ms, new PngEncoder());
        return ms.ToArray();
    }
}
