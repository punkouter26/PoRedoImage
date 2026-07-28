using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PoRedoImage.Application.Features.RapRoast;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Shared.DTOs;

namespace PoRedoImage.Tests.Unit.Features;

/// <summary>
/// Covers the two pieces of the Rap Roast slice that carry real logic: the lyric writer's guardrail
/// and fallback, and the orchestrator's bounded refusal state machine. Both are pure — every
/// collaborator is a stub, so no network call is possible (§5 budget guardrail).
/// </summary>
public class RapRoastTests
{
    private const string Description = "Two friends in matching tracksuits posing by a parked car.";
    private static readonly IReadOnlyList<string> Tags = ["tracksuit", "pose", "car"];

    // ── RoastLyricsWriter ────────────────────────────────────────────

    [Fact]
    public async Task Lyrics_fall_back_to_heuristic_when_no_chat_provider_is_configured()
    {
        var chat = new Mock<IChatCompletionService>();
        chat.SetupGet(c => c.IsConfigured).Returns(false);
        var writer = new RoastLyricsWriter(chat.Object, NullLogger<RoastLyricsWriter>.Instance);

        var result = await writer.WriteAsync(Description, Tags, RapStyle.BoomBap, softened: false);

        // The fallback must be structurally identical to the AI output — the music model relies on
        // the section tags, so a bare paragraph would change how the track is performed.
        Assert.Contains("[Verse]", result.Text, StringComparison.Ordinal);
        Assert.Contains("[Chorus]", result.Text, StringComparison.Ordinal);
        chat.Verify(c => c.CompleteAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Lyric_prompt_forbids_protected_characteristics()
    {
        string? capturedSystem = null;
        var chat = new Mock<IChatCompletionService>();
        chat.SetupGet(c => c.IsConfigured).Returns(true);
        chat.Setup(c => c.CompleteAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, byte[]?, CancellationToken>((sys, _, _, _) => capturedSystem = sys)
            .ReturnsAsync(new ChatCompletionResult("[Verse]\nbars\n[Chorus]\nhook", 10, 5));

        var writer = new RoastLyricsWriter(chat.Object, NullLogger<RoastLyricsWriter>.Instance);
        await writer.WriteAsync(Description, Tags, RapStyle.Trap, softened: false);

        Assert.NotNull(capturedSystem);
        foreach (var forbidden in (string[])["race", "disability", "body weight", "age", "religion"])
        {
            Assert.Contains(forbidden, capturedSystem, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Softened_pass_instructs_a_tamer_tone()
    {
        var userPrompts = new List<string>();
        var chat = new Mock<IChatCompletionService>();
        chat.SetupGet(c => c.IsConfigured).Returns(true);
        chat.Setup(c => c.CompleteAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, byte[]?, CancellationToken>((_, user, _, _) => userPrompts.Add(user))
            .ReturnsAsync(new ChatCompletionResult("[Verse]\nbars\n[Chorus]\nhook", 10, 5));

        var writer = new RoastLyricsWriter(chat.Object, NullLogger<RoastLyricsWriter>.Instance);
        await writer.WriteAsync(Description, Tags, RapStyle.BoomBap, softened: false);
        await writer.WriteAsync(Description, Tags, RapStyle.BoomBap, softened: true);

        Assert.DoesNotContain("SECOND attempt", userPrompts[0], StringComparison.Ordinal);
        Assert.Contains("SECOND attempt", userPrompts[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Markdown_fences_are_stripped_from_model_output()
    {
        var chat = new Mock<IChatCompletionService>();
        chat.SetupGet(c => c.IsConfigured).Returns(true);
        chat.Setup(c => c.CompleteAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatCompletionResult("```\n[Verse]\nbars here\n[Chorus]\nhook\n```", 10, 5));

        var writer = new RoastLyricsWriter(chat.Object, NullLogger<RoastLyricsWriter>.Instance);
        var result = await writer.WriteAsync(Description, Tags, RapStyle.BoomBap, softened: false);

        // A stray fence would otherwise be handed to the music model and performed as a lyric.
        Assert.DoesNotContain("```", result.Text, StringComparison.Ordinal);
        Assert.StartsWith("[Verse]", result.Text, StringComparison.Ordinal);
    }

    // ── RapRoastOrchestrator ─────────────────────────────────────────

    [Fact]
    public async Task Successful_generation_returns_audio_and_calls_the_music_provider_once()
    {
        var music = new Mock<IMusicGenerationService>();
        music.SetupGet(m => m.IsConfigured).Returns(true);
        music.Setup(m => m.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MusicGenerationResult([1, 2, 3], "audio/mpeg", 10));

        var response = await RunAsync(music.Object);

        Assert.False(response.AudioRefused);
        Assert.False(response.LyricsSoftened);
        Assert.Equal("audio/mpeg", response.AudioContentType);
        Assert.NotEmpty(response.AudioData);
        music.Verify(m => m.GenerateAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_refusal_triggers_exactly_one_softened_retry()
    {
        var calls = 0;
        var music = new Mock<IMusicGenerationService>();
        music.SetupGet(m => m.IsConfigured).Returns(true);
        music.Setup(m => m.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++calls == 1
                ? MusicGenerationResult.FromRefusal(5, "Blocked by safety filters.")
                : new MusicGenerationResult([9], "audio/mpeg", 10));

        var response = await RunAsync(music.Object);

        Assert.False(response.AudioRefused);
        Assert.True(response.LyricsSoftened, "the second attempt must use the softened lyrics");
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Persistent_refusal_returns_lyrics_only_within_the_attempt_cap()
    {
        var music = new Mock<IMusicGenerationService>();
        music.SetupGet(m => m.IsConfigured).Returns(true);
        music.Setup(m => m.GenerateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MusicGenerationResult.FromRefusal(5, "Blocked by safety filters."));

        var response = await RunAsync(music.Object);

        // The user must still receive something, and the provider must not be called on a loop.
        Assert.True(response.AudioRefused);
        Assert.Empty(response.AudioData);
        Assert.NotEmpty(response.Lyrics);
        Assert.Equal("Blocked by safety filters.", response.RefusalReason);
        music.Verify(m => m.GenerateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(RapRoastOrchestrator.MaxMusicAttempts));
    }

    [Fact]
    public async Task Unconfigured_music_provider_returns_lyrics_without_calling_it()
    {
        var music = new Mock<IMusicGenerationService>();
        music.SetupGet(m => m.IsConfigured).Returns(false);

        var response = await RunAsync(music.Object);

        Assert.True(response.AudioRefused);
        Assert.NotEmpty(response.Lyrics);
        music.Verify(m => m.GenerateAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>Runs the orchestrator with a stub vision backend and the heuristic lyric path.</summary>
    private static async Task<RapRoastResponse> RunAsync(IMusicGenerationService music)
    {
        var vision = new Mock<IVisionService>();
        vision.Setup(v => v.AnalyzeAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Description, Tags, 0.9, 5L));

        var router = new Mock<IVisionServiceRouter>();
        router.Setup(r => r.Resolve(It.IsAny<string?>())).Returns(vision.Object);

        var chat = new Mock<IChatCompletionService>();
        chat.SetupGet(c => c.IsConfigured).Returns(false);

        var orchestrator = new RapRoastOrchestrator(
            router.Object,
            new RoastLyricsWriter(chat.Object, NullLogger<RoastLyricsWriter>.Instance),
            music,
            NullLogger<RapRoastOrchestrator>.Instance);

        return await orchestrator.ProcessAsync(new RapRoastRequest
        {
            ImageData = Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47]),
            ContentType = "image/png",
        });
    }
}
