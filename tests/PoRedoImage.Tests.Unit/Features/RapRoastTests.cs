using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PoRedoImage.Application.Features.RapRoast;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Infrastructure.Services;
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

        var result = await writer.WriteAsync(Description, Tags, RapStyle.StandUp, RoastIntensity.Roast, softened: false);

        // The fallback must be structurally identical to the AI output — the music model relies on
        // the section tags, so a bare paragraph would change how the track is performed.
        Assert.Contains("[Verse]", result.Text, StringComparison.Ordinal);
        Assert.Contains("[Chorus]", result.Text, StringComparison.Ordinal);

        // And it has to say it is the stock verse. These bars are mild and clean by construction,
        // so an unexplained fallback is indistinguishable from the model deciding to go easy —
        // which is precisely how a content-filter rejection presented before this carried a reason.
        Assert.NotNull(result.FallbackReason);
        Assert.Contains("stock bars", result.FallbackReason, StringComparison.Ordinal);

        // An absent provider is an outage, not censorship. The filter report counts rejections, so
        // conflating the two would show "100% filtered" on a machine with no chat model configured.
        Assert.False(result.FilterRejected);
        chat.Verify(c => c.CompleteAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Prompts_carry_the_guardrail_and_escalate_tone_on_the_softened_pass()
    {
        string? capturedSystem = null;
        var userPrompts = new List<string>();

        var chat = new Mock<IChatCompletionService>();
        chat.SetupGet(c => c.IsConfigured).Returns(true);
        chat.Setup(c => c.CompleteAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, byte[]?, CancellationToken>((sys, user, _, _) =>
            {
                capturedSystem = sys;
                userPrompts.Add(user);
            })
            .ReturnsAsync(new ChatCompletionResult("[Verse]\nbars\n[Chorus]\nhook", 10, 5));

        var writer = new RoastLyricsWriter(chat.Object, NullLogger<RoastLyricsWriter>.Instance);
        await writer.WriteAsync(Description, Tags, RapStyle.StandUp, RoastIntensity.Roast, softened: false);
        await writer.WriteAsync(Description, Tags, RapStyle.StandUp, RoastIntensity.Roast, softened: true);
        var standUpSystem = capturedSystem;
        await writer.WriteAsync(Description, Tags, RapStyle.Trap, RoastIntensity.Roast, softened: false);
        var rapSystem = capturedSystem;

        // The guardrail is what keeps the roast on choices rather than characteristics — and what
        // keeps the music provider's safety filter from refusing the track outright.
        Assert.NotNull(capturedSystem);
        foreach (var forbidden in (string[])["race", "disability", "body weight", "age", "religion"])
        {
            Assert.Contains(forbidden, capturedSystem, StringComparison.OrdinalIgnoreCase);
        }

        // Only the retry pass tells the model it was already rejected.
        Assert.DoesNotContain("SECOND attempt", userPrompts[0], StringComparison.Ordinal);
        Assert.Contains("SECOND attempt", userPrompts[1], StringComparison.Ordinal);

        // Delivery changes the FORM, not just a descriptive line. Stand-up asks for prose
        // punchlines and explicitly un-asks for rhyme; the rap styles still demand it. Without
        // this, picking Stand-up would have produced rapped bars under a different button.
        Assert.NotNull(standUpSystem);
        Assert.NotNull(rapSystem);
        Assert.Contains("stand-up comedian", standUpSystem, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Rhyme is not required", standUpSystem, StringComparison.Ordinal);
        Assert.DoesNotContain("scan to a beat", standUpSystem, StringComparison.Ordinal);
        Assert.Contains("battle rap", rapSystem, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("scan to a beat", rapSystem, StringComparison.Ordinal);

        // Both forms keep the section tags: Lyria performs what they delimit and the karaoke
        // highlighting derives its line timings from them.
        Assert.Contains("[Verse]", standUpSystem, StringComparison.Ordinal);
        Assert.Contains("[Chorus]", standUpSystem, StringComparison.Ordinal);
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
        var result = await writer.WriteAsync(Description, Tags, RapStyle.StandUp, RoastIntensity.Roast, softened: false);

        // A stray fence would otherwise be handed to the music model and performed as a lyric.
        Assert.DoesNotContain("```", result.Text, StringComparison.Ordinal);
        Assert.StartsWith("[Verse]", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Intensity_steers_the_tone_direction_and_the_retry_steps_it_down_one_stop()
    {
        var userPrompts = new List<string>();

        var chat = new Mock<IChatCompletionService>();
        chat.SetupGet(c => c.IsConfigured).Returns(true);
        chat.Setup(c => c.CompleteAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, byte[]?, CancellationToken>((_, user, _, _) => userPrompts.Add(user))
            .ReturnsAsync(new ChatCompletionResult("[Verse]\nbars\n[Chorus]\nhook", 10, 5));

        var writer = new RoastLyricsWriter(chat.Object, NullLogger<RoastLyricsWriter>.Instance);
        await writer.WriteAsync(Description, Tags, RapStyle.StandUp, RoastIntensity.Gentle, softened: false);
        await writer.WriteAsync(Description, Tags, RapStyle.StandUp, RoastIntensity.Scorched, softened: false);
        await writer.WriteAsync(Description, Tags, RapStyle.StandUp, RoastIntensity.Scorched, softened: true);
        await writer.WriteAsync(Description, Tags, RapStyle.StandUp, RoastIntensity.Nuclear, softened: false);

        // The dial has to reach the model, or it is a control that does nothing.
        Assert.Contains("affectionate", userPrompts[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("destroy them", userPrompts[1], StringComparison.OrdinalIgnoreCase);

        // Turning it up must never widen what may be targeted — Scorched is a harsher delivery at
        // the same targets, and the prompt says so out loud because that is when a model reaches
        // for the cheap shot.
        Assert.Contains("CHOICE", userPrompts[1], StringComparison.Ordinal);

        // A refusal steps the dial down one stop rather than collapsing to the mildest setting:
        // Scorched retries as Roast, so the user still gets close to the track they asked for.
        Assert.Contains("SECOND attempt", userPrompts[2], StringComparison.Ordinal);
        Assert.Contains("good-natured", userPrompts[2], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("destroy them", userPrompts[2], StringComparison.OrdinalIgnoreCase);
        // The ceiling has to read as a different instruction from Scorched, or it is a label.
        Assert.Contains("ceiling", userPrompts[3], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hold nothing back", userPrompts[3], StringComparison.OrdinalIgnoreCase);

        Assert.Equal(RoastIntensity.Scorched, RoastLyricsWriter.StepDown(RoastIntensity.Nuclear));
        Assert.Equal(RoastIntensity.Roast, RoastLyricsWriter.StepDown(RoastIntensity.Scorched));
        Assert.Equal(RoastIntensity.Gentle, RoastLyricsWriter.StepDown(RoastIntensity.Gentle));
    }

    [Fact]
    public async Task Explicit_language_moves_only_the_language_rule_and_the_retry_gives_it_up_first()
    {
        var systemPrompts = new List<string>();
        var userPrompts = new List<string>();

        var chat = new Mock<IChatCompletionService>();
        chat.SetupGet(c => c.IsConfigured).Returns(true);
        chat.Setup(c => c.CompleteAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, byte[]?, CancellationToken>((sys, user, _, _) =>
            {
                systemPrompts.Add(sys);
                userPrompts.Add(user);
            })
            .ReturnsAsync(new ChatCompletionResult("[Verse]\nbars\n[Chorus]\nhook", 10, 5));

        var writer = new RoastLyricsWriter(chat.Object, NullLogger<RoastLyricsWriter>.Instance);
        await writer.WriteAsync(Description, Tags, RapStyle.StandUp, RoastIntensity.Scorched, softened: false);
        await writer.WriteAsync(
            Description, Tags, RapStyle.StandUp, RoastIntensity.Scorched, softened: false, explicitLanguage: true);
        var retry = await writer.WriteAsync(
            Description, Tags, RapStyle.StandUp, RoastIntensity.Scorched, softened: true, explicitLanguage: true);

        // Clean is the default: the toggle has to be asked for, not merely not-refused.
        Assert.Contains("no profanity", systemPrompts[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EXPLICIT MODE", systemPrompts[1], StringComparison.Ordinal);

        // The whole point of splitting the constant: turning the language up must not move the
        // targets. Both prompts carry the identical target guardrail, slur ban included.
        Assert.Contains(RoastLyricsWriter.TargetGuardrail, systemPrompts[0], StringComparison.Ordinal);
        Assert.Contains(RoastLyricsWriter.TargetGuardrail, systemPrompts[1], StringComparison.Ordinal);
        Assert.Contains("slur", systemPrompts[1], StringComparison.OrdinalIgnoreCase);

        // The craft rules ride on every prompt regardless of dial. A live Scorched run returned an
        // accurate inventory of the photo with no jokes in it, so "each line is a punchline, not a
        // description" is load-bearing, not decoration.
        Assert.Contains("must be a JOKE", systemPrompts[0], StringComparison.Ordinal);
        Assert.Contains("must be a JOKE", systemPrompts[1], StringComparison.Ordinal);

        // A refused explicit draft concedes the swearing, NOT the intensity — the user keeps the
        // Scorched punchlines they asked for and only loses the words.
        Assert.Contains("no profanity", systemPrompts[2], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("destroy them", userPrompts[2], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("explicit language has been switched off", userPrompts[2], StringComparison.Ordinal);
        Assert.True(retry.ExplicitDropped);
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
            .ReturnsAsync((Description, Tags, 0.9, 5L, (string?)null));

        var router = new Mock<IVisionServiceRouter>();
        router.Setup(r => r.Resolve(It.IsAny<string?>())).Returns(vision.Object);

        var chat = new Mock<IChatCompletionService>();
        chat.SetupGet(c => c.IsConfigured).Returns(false);

        var orchestrator = new RapRoastOrchestrator(
            router.Object,
            // Chat is unconfigured and no scene detail is available, so the describer returns the
            // vision backend's text unchanged — these tests are about the refusal state machine.
            new SceneDescriber(
                chat.Object,
                new NullSceneDetailProvider(),
                Mock.Of<IGenerativeAiService>(),
                new ConfigurationBuilder().Build(),
                NullLogger<SceneDescriber>.Instance),
            new RoastLyricsWriter(chat.Object, NullLogger<RoastLyricsWriter>.Instance),
            music,
            // NullSceneDetailProvider reports SupportsCombinedAnalysis=false, so the orchestrator
            // takes its two-call-in-parallel path — which is what these tests want exercised.
            new NullSceneDetailProvider(),
            NullLogger<RapRoastOrchestrator>.Instance);

        return await orchestrator.ProcessAsync(new RapRoastRequest
        {
            ImageData = Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47]),
            ContentType = "image/png",
        });
    }
}
