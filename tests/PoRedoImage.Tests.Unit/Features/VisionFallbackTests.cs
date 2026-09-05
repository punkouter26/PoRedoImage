using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Infrastructure.Services;

namespace PoRedoImage.Tests.Unit.Features;

/// <summary>
/// Guards the two halves of the Computer Vision caption fallback: that the reason travels to the
/// caller, and that it survives the cache. Both were silent before — a region without Caption
/// support returned tag-derived text on every request with nothing to say so.
/// </summary>
public class VisionFallbackTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

    [Theory]
    [InlineData(null)]
    [InlineData(AzureVisionService.CaptionUnsupportedReason)]
    public async Task Cache_round_trips_the_fallback_reason(string? reason)
    {
        var inner = new Mock<IVisionService>();
        inner.Setup(v => v.AnalyzeAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("a cat", (IReadOnlyList<string>)["cat"], 0.9, 800L, reason));

        var sut = new CachingVisionService(
            inner.Object, new MemoryCache(new MemoryCacheOptions()),
            NullLogger<CachingVisionService>.Instance, "vision:test");

        var miss = await sut.AnalyzeAsync(Png);
        var hit = await sut.AnalyzeAsync([.. Png]);   // equal content, different array instance

        Assert.Equal(reason, miss.FallbackReason);

        // The cache hit must carry the reason too. Serving the degraded description without its
        // explanation would reintroduce exactly the silent degradation the field exists to prevent.
        Assert.Equal(reason, hit.FallbackReason);
        inner.Verify(v => v.AnalyzeAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Fallback_reasons_are_user_facing_prose_not_error_codes()
    {
        // These strings are rendered verbatim to the user in ImageRegeneration/MemeGeneration.
        foreach (var reason in new[]
                 {
                     AzureVisionService.CaptionUnsupportedReason,
                     AzureVisionService.NoCaptionReason,
                 })
        {
            Assert.EndsWith(".", reason, StringComparison.Ordinal);
            Assert.DoesNotContain("Exception", reason, StringComparison.OrdinalIgnoreCase);
            Assert.True(reason.Split(' ').Length >= 8, $"Too terse to explain anything: '{reason}'");
        }
    }
}
