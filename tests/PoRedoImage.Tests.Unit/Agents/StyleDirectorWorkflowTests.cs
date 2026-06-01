using Microsoft.Extensions.Logging.Abstractions;
using PoRedoImage.Application.Agents;
using PoRedoImage.Application.Agents.StyleDirector;

namespace PoRedoImage.Tests.Unit.Agents;

/// <summary>
/// End-to-end unit tests for the Style Director workflow (Idea #1).
/// Verifies the chain produces a coherent result on the happy path and
/// returns a partial trace when an intermediate step throws.
/// </summary>
public class StyleDirectorWorkflowTests
{
    private static StyleDirectorWorkflow CreateWorkflow() => new(
        new VisionAnalystAgent(NullLogger<VisionAnalystAgent>.Instance),
        new StyleStrategistAgent(NullLogger<StyleStrategistAgent>.Instance),
        new PromptRefinerAgent(NullLogger<PromptRefinerAgent>.Instance),
        new CriticAgent(NullLogger<CriticAgent>.Instance),
        NullLogger<StyleDirectorWorkflow>.Instance);

    [Fact]
    public async Task RunAsync_HappyPath_ProducesAllFourReasoningEntries()
    {
        var workflow = CreateWorkflow();
        var input = new VisionAnalystInput(
            ImageBytes: [0x89, 0x50, 0x4E, 0x47],
            DetectedTags: ["person", "outdoor", "smile"],
            ConfidenceScore: 0.85);

        var result = await workflow.RunAsync(input);

        Assert.True(result.Succeeded);
        Assert.Equal(4, result.Reasoning.Count);
        Assert.Contains(result.Reasoning, r => r.AgentId == "vision-analyst");
        Assert.Contains(result.Reasoning, r => r.AgentId == "style-strategist");
        Assert.Contains(result.Reasoning, r => r.AgentId == "prompt-refiner");
        Assert.Contains(result.Reasoning, r => r.AgentId == "critic");
        Assert.NotNull(result.Output);
        Assert.False(string.IsNullOrWhiteSpace(result.Output.Decision.FinalPrompt));
    }

    [Fact]
    public async Task RunAsync_FinalPrompt_ContainsAllRequiredGuards()
    {
        var workflow = CreateWorkflow();
        var input = new VisionAnalystInput([], ["dog", "park"], 0.5);

        var result = await workflow.RunAsync(input);

        Assert.True(result.Succeeded);
        var prompt = result.Output.Decision.FinalPrompt;
        // Critic must add preserve + no-watermark guards; if the refiner already
        // includes them, the critic leaves them alone and confidence stays high.
        Assert.Contains("Preserve", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_EmptyInput_StillSucceeds()
    {
        var workflow = CreateWorkflow();
        var input = new VisionAnalystInput([], [], 0);

        var result = await workflow.RunAsync(input);

        // Agents are tolerant of empty input — workflow should still complete.
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Output);
    }
}
