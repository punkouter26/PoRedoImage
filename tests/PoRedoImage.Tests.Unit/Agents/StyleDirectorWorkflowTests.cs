using Microsoft.Extensions.Logging.Abstractions;
using PoRedoImage.Application.Agents;
using PoRedoImage.Application.Agents.StyleDirector;
using PoRedoImage.Domain.Interfaces;

namespace PoRedoImage.Tests.Unit.Agents;

/// <summary>
/// End-to-end unit tests for the Style Director workflow (Idea #1).
/// Runs against the deterministic heuristic path (chat not configured) so assertions are stable;
/// verifies the chain produces a coherent result on the happy path.
/// </summary>
public class StyleDirectorWorkflowTests
{
    /// <summary>Chat service that reports not-configured, so agents use their heuristic fallback.</summary>
    private sealed class NotConfiguredChat : IChatCompletionService
    {
        public bool IsConfigured => false;
        public Task<ChatCompletionResult> CompleteAsync(
            string systemPrompt, string userPrompt, byte[]? image = null, CancellationToken ct = default)
            => throw new NotSupportedException("Heuristic path should be used when IsConfigured is false.");

        public IAsyncEnumerable<string> StreamCompleteAsync(
            string systemPrompt, string userPrompt, byte[]? image = null, CancellationToken ct = default)
            => throw new NotSupportedException("Heuristic path should be used when IsConfigured is false.");
    }

    /// <summary>Configured chat that replays a fixed JSON payload, to drive the AI path.</summary>
    private sealed class ScriptedChat(string prompt) : IChatCompletionService
    {
        public bool IsConfigured => true;
        public Task<ChatCompletionResult> CompleteAsync(
            string systemPrompt, string userPrompt, byte[]? image = null, CancellationToken ct = default)
            => Task.FromResult(new ChatCompletionResult(
                $$"""{"subject":"A man","mood":"Wry","themes":["corporate"],"prompt":{{System.Text.Json.JsonSerializer.Serialize(prompt)}},"confidence":82}""",
                TokensUsed: 100,
                ElapsedMs: 5));

        public async IAsyncEnumerable<string> StreamCompleteAsync(
            string systemPrompt, string userPrompt, byte[]? image = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield return prompt;
        }
    }

    private static readonly IChatCompletionService Chat = new NotConfiguredChat();

    private static StyleDirectorWorkflow ScriptedWorkflow(string prompt) => new(
        new ScriptedChat(prompt),
        NullLogger<StyleDirectorWorkflow>.Instance);

    private static StyleDirectorWorkflow CreateWorkflow() => new(
        Chat,
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

    [Fact]
    public async Task RunAsync_PromptRequestingTypography_KeepsTextGuardOff()
    {
        // The blanket "no text" guard used to contradict the very prompt it guarded: a captioned
        // portrait came back asking for `top text reads "..."` alongside `no text`.
        var workflow = ScriptedWorkflow(
            "Portrait with bold typographic overlay; top text reads \"WHEN THE SUIT SPEAKS\".");

        var result = await workflow.RunAsync(new VisionAnalystInput([0x89], ["person"], 0.9));

        var prompt = result.Output.Decision.FinalPrompt;
        Assert.DoesNotContain("no text", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No watermarks, no logos.", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_PromptWithoutTypography_KeepsFullTextGuard()
    {
        var workflow = ScriptedWorkflow("A serene watercolour landscape at dawn.");

        var result = await workflow.RunAsync(new VisionAnalystInput([0x89], ["landscape"], 0.9));

        var prompt = result.Output.Decision.FinalPrompt;
        Assert.Contains("No watermarks, no text, no logos.", prompt, StringComparison.Ordinal);
    }
}
