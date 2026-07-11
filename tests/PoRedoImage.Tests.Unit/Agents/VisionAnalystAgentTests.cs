using Microsoft.Extensions.Logging.Abstractions;
using PoRedoImage.Application.Agents.StyleDirector;
using PoRedoImage.Domain.Interfaces;

namespace PoRedoImage.Tests.Unit.Agents;

/// <summary>
/// Unit tests for the Style Director workflow agents (Idea #1).
/// A not-configured chat service forces the agents down their deterministic heuristic path, which is
/// what these tests assert; the real-AI path is exercised end-to-end against the live provider.
/// </summary>
public class StyleDirectorAgentTests
{
    /// <summary>Chat service that reports not-configured, so agents use their heuristic fallback.</summary>
    private sealed class NotConfiguredChat : IChatCompletionService
    {
        public bool IsConfigured => false;
        public Task<ChatCompletionResult> CompleteAsync(
            string systemPrompt, string userPrompt, byte[]? image = null, CancellationToken ct = default)
            => throw new NotSupportedException("Heuristic path should be used when IsConfigured is false.");
    }

    private static readonly IChatCompletionService Chat = new NotConfiguredChat();
    // ─── Vision Analyst ────────────────────────────────────────────

    [Fact]
    public async Task VisionAnalyst_OutdoorTags_InfersSereneMood()
    {
        var agent = new VisionAnalystAgent(Chat, NullLogger<VisionAnalystAgent>.Instance);
        var input = new VisionAnalystInput(
            ImageBytes: [0x89, 0x50, 0x4E, 0x47],
            DetectedTags: ["outdoor", "mountain", "sky"],
            ConfidenceScore: 0.9);

        var result = await agent.RunAsync(input);

        Assert.Equal("vision-analyst", result.Reasoning.AgentId);
        Assert.Equal("serene", result.Output.Mood);
        Assert.Contains("mountain", result.Output.Subject);
        Assert.NotEmpty(result.Output.Themes);
    }

    [Fact]
    public async Task VisionAnalyst_NoTags_FallsBackToNeutral()
    {
        var agent = new VisionAnalystAgent(Chat, NullLogger<VisionAnalystAgent>.Instance);
        var input = new VisionAnalystInput([], [], 0);

        var result = await agent.RunAsync(input);

        Assert.Equal("neutral", result.Output.Mood);
        Assert.Equal("scene", result.Output.Subject);
    }

    [Fact]
    public async Task VisionAnalyst_NullTags_Handled()
    {
        var agent = new VisionAnalystAgent(Chat, NullLogger<VisionAnalystAgent>.Instance);
        var input = new VisionAnalystInput([], null!, 0);

        var result = await agent.RunAsync(input);

        Assert.NotNull(result.Output);
    }

    // ─── Style Strategist ──────────────────────────────────────────

    [Fact]
    public async Task StyleStrategist_Moody_ReturnsCyberpunkDirection()
    {
        var agent = new StyleStrategistAgent(Chat, NullLogger<StyleStrategistAgent>.Instance);
        var input = new StyleStrategistInput(new VisionAnalystOutput("city", "moody", ["night"]));

        var result = await agent.RunAsync(input);

        Assert.NotEmpty(result.Output.Directions);
        Assert.Contains(result.Output.Directions, d => d.Name.Contains("Cyberpunk", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("moody", result.Output.Rationale, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StyleStrategist_UnknownMood_ReturnsFallback()
    {
        var agent = new StyleStrategistAgent(Chat, NullLogger<StyleStrategistAgent>.Instance);
        var input = new StyleStrategistInput(new VisionAnalystOutput("subject", "weird-unknown-mood", []));

        var result = await agent.RunAsync(input);

        Assert.NotEmpty(result.Output.Directions);
    }

    // ─── Prompt Refiner ────────────────────────────────────────────

    [Fact]
    public async Task PromptRefiner_BuildsPreserveGuard()
    {
        var agent = new PromptRefinerAgent(Chat, NullLogger<PromptRefinerAgent>.Instance);
        var input = new PromptRefinerInput(new StyleStrategistOutput(
            Directions: [new StyleDirection("Studio Ghibli Watercolor", "soft pastels", "soft brush", "1980s")],
            Rationale: "test"));

        var result = await agent.RunAsync(input);

        Assert.Contains("Preserve", result.Output.Prompt);
        Assert.Contains("Studio Ghibli Watercolor", result.Output.Prompt);
        Assert.Contains("1980s", result.Output.Prompt);
    }

    [Fact]
    public async Task PromptRefiner_NoDirections_StillProducesPrompt()
    {
        var agent = new PromptRefinerAgent(Chat, NullLogger<PromptRefinerAgent>.Instance);
        var input = new PromptRefinerInput(new StyleStrategistOutput([], "empty"));

        var result = await agent.RunAsync(input);

        // Falls back to default direction; prompt still valid.
        Assert.Contains("Preserve", result.Output.Prompt);
    }

    // ─── Critic ────────────────────────────────────────────────────

    [Fact]
    public async Task Critic_ShortPrompt_AddsLength()
    {
        var agent = new CriticAgent(Chat, NullLogger<CriticAgent>.Instance);
        var input = new CriticInput(new PromptRefinerOutput("short prompt", "because"));

        var result = await agent.RunAsync(input);

        Assert.True(result.Output.FinalPrompt.Length > 12);
        Assert.NotEmpty(result.Output.Suggestions);
    }

    [Fact]
    public async Task Critic_GoodPrompt_HighConfidence()
    {
        var agent = new CriticAgent(Chat, NullLogger<CriticAgent>.Instance);
        var good = "Reimagine the subject as a Studio Ghibli Watercolor illustration. Use a soft pastels palette, soft brush. " +
                   "Preserve the subject's facial features and likeness. No watermarks, no text, no logos. Detailed textures, balanced composition.";
        var input = new CriticInput(new PromptRefinerOutput(good, "well-built"));

        var result = await agent.RunAsync(input);

        Assert.True(result.Output.Confidence >= 70, $"Expected >= 70, got {result.Output.Confidence}");
    }

    [Fact]
    public async Task Critic_Confidence_AlwaysInRange()
    {
        var agent = new CriticAgent(Chat, NullLogger<CriticAgent>.Instance);
        var input = new CriticInput(new PromptRefinerOutput("x", "y"));

        var result = await agent.RunAsync(input);

        Assert.InRange(result.Output.Confidence, 0, 100);
    }
}
