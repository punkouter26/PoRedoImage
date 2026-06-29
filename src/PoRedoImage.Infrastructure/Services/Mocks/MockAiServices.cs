using PoRedoImage.Domain.Interfaces;

namespace PoRedoImage.Infrastructure.Services.Mocks;

// Mock AI service implementations used when Mocks:UseMockAi is enabled (Development demos and the
// automated-test tier). Each implements IMockable so the Blazor client renders the "USING MOCK DATA"
// banner, and — critically — none of them make a network call, guaranteeing zero live token spend
// against Azure OpenAI / Computer Vision / Google Gemini. See InfrastructureServiceExtensions.

/// <summary>Canned vision analysis — never calls Azure Computer Vision.</summary>
public sealed class MockVisionService : IVisionService, IMockable
{
    public string MockReason => "Computer Vision (mock)";

    public Task<(string Description, IReadOnlyList<string> Tags, double ConfidenceScore, long ElapsedMs)>
        AnalyzeAsync(byte[] imageData, CancellationToken ct = default)
        => Task.FromResult((
            "A mock analysis of the uploaded image — generated locally with no AI call.",
            (IReadOnlyList<string>)["mock", "sample", "placeholder"],
            0.99,
            1L));
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
