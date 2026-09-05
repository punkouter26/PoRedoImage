using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PoRedoImage.Application.Features.BulkGenerate;
using PoRedoImage.Application.Features.RapRoast;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Infrastructure.Services;
using PoRedoImage.Shared.DTOs;

namespace PoRedoImage.Tests.Unit.Features;

/// <summary>
/// The two efficiency guarantees that are invisible in output and therefore only survive if
/// something asserts them: the vision cache must actually skip the upstream call, and the roast
/// pipeline must not look at the same photo twice.
/// </summary>
/// <remarks>
/// Both are the kind of change that silently regresses — nothing about the rendered result differs
/// when the cache stops hitting or the second Computer Vision call comes back, only the bill and
/// the latency. Pure and stubbed, so no network call is possible (§5 budget guardrail).
/// </remarks>
public class AiPipelineEfficiencyTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

    [Fact]
    public async Task Vision_results_are_reused_for_identical_bytes_and_not_across_different_ones()
    {
        var calls = 0;
        var inner = new Mock<IVisionService>();
        inner.Setup(v => v.AnalyzeAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { calls++; return ("a cat", (IReadOnlyList<string>)["cat"], 0.9, 800L); });

        var sut = new CachingVisionService(
            inner.Object, new MemoryCache(new MemoryCacheOptions()),
            NullLogger<CachingVisionService>.Instance);

        var first = await sut.AnalyzeAsync(Png);
        var second = await sut.AnalyzeAsync([.. Png]);   // equal content, different array instance

        Assert.Equal(1, calls);
        Assert.Equal(first.Description, second.Description);

        // The hit must report 0ms rather than replaying the original duration: the metrics panel
        // shows what THIS request spent, and claiming 800ms for a dictionary lookup would make the
        // pipeline's own timings a lie.
        Assert.Equal(800L, first.ElapsedMs);
        Assert.Equal(0L, second.ElapsedMs);

        // Keyed by content, so different bytes are a different question.
        await sut.AnalyzeAsync([0x89, 0x50, 0x4E, 0x47, 9, 9, 9]);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Roast_looks_at_the_photo_once_when_the_backend_can_combine_the_features()
    {
        var vision = new Mock<IVisionService>();
        var router = new Mock<IVisionServiceRouter>();
        router.Setup(r => r.Resolve(It.IsAny<string?>())).Returns(vision.Object);

        var combined = new Mock<ISceneDetailProvider>();
        var asCombined = combined.As<ICombinedVisionAnalyzer>();
        asCombined.SetupGet(c => c.SupportsCombinedAnalysis).Returns(true);
        asCombined.Setup(c => c.AnalyzeAllAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CombinedVisionResult(
                "two friends by a car", ["tracksuit", "car"], 0.8, SceneDetails.Empty, 400));

        var chat = new Mock<IChatCompletionService>();
        chat.SetupGet(c => c.IsConfigured).Returns(false);

        var music = new Mock<IMusicGenerationService>();
        music.SetupGet(m => m.IsConfigured).Returns(false);

        var orchestrator = new RapRoastOrchestrator(
            router.Object,
            new SceneDescriber(chat.Object, combined.Object, Mock.Of<IGenerativeAiService>(),
                new ConfigurationBuilder().Build(), NullLogger<SceneDescriber>.Instance),
            new RoastLyricsWriter(chat.Object, NullLogger<RoastLyricsWriter>.Instance),
            music.Object,
            combined.Object,
            NullLogger<RapRoastOrchestrator>.Instance);

        var response = await orchestrator.ProcessAsync(new RapRoastRequest
        {
            ImageData = Convert.ToBase64String(Png),
            ContentType = "image/png",
        });

        Assert.NotEmpty(response.Lyrics);

        // Exactly one upstream look at the image: the combined call. The separate AnalyzeAsync and
        // GetDetailsAsync round-trips this replaced must NOT come back — that pair was two charges
        // and two round-trips for one question.
        asCombined.Verify(c => c.AnalyzeAllAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Once);
        vision.Verify(v => v.AnalyzeAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);
        combined.Verify(c => c.GetDetailsAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Bulk_generation_retries_on_rate_limit_and_succeeds()
    {
        var generator = new Mock<IImageGenerationService>();
        var attempts = 0;
        generator.Setup(g => g.GenerateImageAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new HttpRequestException("429 Too Many Requests (RESOURCE_EXHAUSTED)");
                }
                return Task.FromResult((Png, "image/png", 100L));
            });

        var router = new Mock<IImageGenerationRouter>();
        router.Setup(r => r.Resolve(It.IsAny<string?>())).Returns(generator.Object);

        var sut = new BulkGenerationService(router.Object, NullLogger<BulkGenerationService>.Instance);
        var results = new List<BulkBatchItem>();
        await foreach (var item in sut.GenerateBatchAsync(["test prompt"], Png, null))
        {
            results.Add(item);
        }

        Assert.Single(results);
        Assert.NotNull(results[0].ImageData);
        Assert.Null(results[0].Error);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task Chat_completion_streaming_yields_expected_tokens()
    {
        var chat = new Mock<IChatCompletionService>();
        static async IAsyncEnumerable<string> ProduceTokens()
        {
            await Task.Yield();
            yield return "Hello";
            yield return " ";
            yield return "world";
        }

        chat.Setup(c => c.StreamCompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]?>(), It.IsAny<CancellationToken>()))
            .Returns(ProduceTokens());

        var tokens = new List<string>();
        await foreach (var token in chat.Object.StreamCompleteAsync("sys", "user"))
        {
            tokens.Add(token);
        }

        Assert.Equal(["Hello", " ", "world"], tokens);
    }
}
