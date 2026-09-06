using PoRedoImage.Domain.Interfaces;

namespace PoRedoImage.Infrastructure.Services.Mocks;

// Mock AI service implementations wired when MockAiGate.IsEnabled(...) returns true — gated to the
// Test environment only (integration + E2E). Development and Production MUST use real services;
// mocks are a test fixture, not a dev shortcut. Each implementation also implements IMockable so the
// Blazor client renders the "USING MOCK DATA" banner, and — critically — none make a network call,
// guaranteeing zero live token spend against Azure OpenAI / Computer Vision / Google Gemini.

/// <summary>Canned vision analysis — never calls Azure Computer Vision.</summary>
public sealed class MockVisionService : IVisionService, IMockable
{
    public string MockReason => "Computer Vision (mock)";

    public Task<(string Description, IReadOnlyList<string> Tags, double ConfidenceScore, long ElapsedMs, string? FallbackReason)>
        AnalyzeAsync(byte[] imageData, CancellationToken ct = default)
        => Task.FromResult((
            "A mock analysis of the uploaded image — generated locally with no AI call.",
            (IReadOnlyList<string>)["mock", "sample", "placeholder"],
            0.99,
            1L,
            (string?)null));
}

/// <summary>Canned text generation — never calls Azure OpenAI.</summary>
public sealed class MockGenerativeAiService : IGenerativeAiService, IMockable
{
    public string MockReason => "OpenAI text (mock)";

    public Task<(string EnhancedDescription, int TokensUsed, long ElapsedMs)>
        EnhanceDescriptionAsync(string description, IReadOnlyList<string> tags, int targetLength, CancellationToken ct = default)
        => Task.FromResult(($"[MOCK] {description}", 0, 1L));

    public Task<(string TopText, string BottomText, int TokensUsed, long ElapsedMs)>
        GenerateMemeCaptionAsync(IReadOnlyList<string> tags, CancellationToken ct = default)
        => Task.FromResult(("MOCK TOP TEXT", "MOCK BOTTOM TEXT", 0, 1L));

    public Task<string> DescribePersonAsync(byte[] imageData, CancellationToken ct = default)
        => Task.FromResult("A mock person description — generated locally with no AI call.");
}

/// <summary>
/// Canned chat completion — never calls a live provider. Reports <see cref="IsConfigured"/> == false so
/// the Style Director agents deterministically fall back to their heuristic path (zero network, stable
/// output for the automated-test tier).
/// </summary>
public sealed class MockChatCompletionService : IChatCompletionService, IMockable
{
    public string MockReason => "Chat completion (mock)";

    public bool IsConfigured => false;

    public Task<ChatCompletionResult> CompleteAsync(
        string systemPrompt, string userPrompt, byte[]? image = null, CancellationToken ct = default)
        => throw new InvalidOperationException(
            "MockChatCompletionService.CompleteAsync should never be called — IsConfigured is false so "
            + "callers must use their heuristic fallback. Reaching here indicates a missing IsConfigured guard.");

    public IAsyncEnumerable<string> StreamCompleteAsync(
        string systemPrompt, string userPrompt, byte[]? image = null, CancellationToken ct = default)
        => throw new InvalidOperationException(
            "MockChatCompletionService.StreamCompleteAsync should never be called — IsConfigured is false.");
}

/// <summary>Canned image generation — returns a tiny PNG and never calls Google Gemini/Imagen.</summary>
public sealed class MockImagen3Service : IImageGenerationService, IMockable
{
    public string MockReason => "Imagen3 image-gen (mock)";

    public bool IsConfigured => true;

    // 1×1 opaque PNG — enough for the UI to render a data URL without any upstream call.
    private static readonly byte[] PixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    public Task<(byte[] ImageData, string ContentType, long ElapsedMs)>
        GenerateAsync(string prompt, CancellationToken ct = default)
        => Task.FromResult((PixelPng, "image/png", 1L));

    public Task<(byte[] ImageData, string ContentType, long ElapsedMs)>
        GenerateImageAsync(string prompt, byte[] imageBytes, CancellationToken ct = default)
        => Task.FromResult((PixelPng, "image/png", 1L));

    public Task<(byte[] ImageData, string ContentType, long ElapsedMs)>
        GenerateImageAsync(string prompt, byte[] imageBytes, int seed, CancellationToken ct = default)
        => Task.FromResult((PixelPng, "image/png", 1L));
}

/// <summary>
/// Canned music generation — returns a tiny silent MP3 and never calls Google Lyria.
/// </summary>
/// <remarks>
/// Registered against <see cref="IMusicGenerationService"/>, the interface the orchestrator
/// actually resolves. The audit found the opposite mistake elsewhere: mocking a concrete service
/// while the caller went through a router meant the real backend was still being hit.
/// </remarks>
public sealed class MockLyriaMusicService : IMusicGenerationService, IMockable
{
    public string MockReason => "Lyria music-gen (mock)";

    public bool IsConfigured => true;

    // Minimal silent MP3 frame — enough for an <audio> element to load without any upstream call.
    private static readonly byte[] SilentMp3 = Convert.FromBase64String(
        "SUQzBAAAAAAAI1RTU0UAAAAPAAADTGF2ZjU4Ljc2LjEwMAAAAAAAAAAAAAAA//tQxAADwAAB"
        + "pAAAACAAADSAAAAETEFNRTMuMTAwVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVV"
        + "VVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVV");

    public Task<MusicGenerationResult> GenerateAsync(
        string lyrics, string stylePrompt, CancellationToken ct = default)
        => Task.FromResult(new MusicGenerationResult(SilentMp3, "audio/mpeg", 1L));
}

/// <summary>
/// Music service that always reports a safety refusal. Not registered by default — it exists so the
/// orchestrator's soften-and-retry path can be exercised without a network call.
/// </summary>
public sealed class AlwaysRefusingMusicService : IMusicGenerationService, IMockable
{
    public string MockReason => "Lyria music-gen (always refuses)";

    public bool IsConfigured => true;

    public int AttemptCount { get; private set; }

    public Task<MusicGenerationResult> GenerateAsync(
        string lyrics, string stylePrompt, CancellationToken ct = default)
    {
        AttemptCount++;
        return Task.FromResult(MusicGenerationResult.FromRefusal(1L, "Blocked by safety filters."));
    }
}

/// <summary>
/// Video generator that returns a tiny valid MP4 without any upstream call.
/// </summary>
/// <remarks>
/// The budget guardrail matters most here: Veo is metered per second of output, so a single
/// accidental live call in a test run costs more than every other mocked service combined. The
/// job completes on the first poll — the real service takes 1–5 minutes, and making the E2E suite
/// wait that long to assert a UI transition would buy nothing.
/// </remarks>
public sealed class MockVeoVideoGenerationService : IVideoGenerationService, IMockable
{
    public string MockReason => "Veo video-gen (mock)";

    public bool IsConfigured => true;

    /// <summary>
    /// Smallest well-formed MP4 container: an ftyp box (isom) followed by an empty moov box.
    /// Enough for a &lt;video&gt; element to accept the source and for a download to produce a real file.
    /// </summary>
    private static readonly byte[] TinyMp4 = Convert.FromBase64String(
        "AAAAHGZ0eXBpc29tAAACAGlzb21pc28ybXA0MQAAAAhtb292");

    public Task<string> StartAsync(
        byte[] image, string contentType, string prompt, CancellationToken ct = default)
        => Task.FromResult("models/veo-mock/operations/mock-video-operation");

    public Task<VideoGenerationStatus> PollAsync(string operationName, CancellationToken ct = default)
        => Task.FromResult(new VideoGenerationStatus(Done: true, Video: TinyMp4, ContentType: "video/mp4"));
}
