using PoRedoImage.Application.Agents;
using PoRedoImage.Application.Agents.StyleDirector;
using PoRedoImage.Shared.DTOs;
using PoRedoImage.Shared.Imaging;
using PoRedoImage.Web.Features.Shared;

namespace PoRedoImage.Web.Features.StyleDirector;

/// <summary>
/// Minimal API endpoint for the Agentic Style Director (Idea #1).
/// POST /api/style-director/run → runs the 4-agent chain and returns the
/// final prompt + full reasoning trace for the UI to render.
/// </summary>
public static class StyleDirectorEndpoints
{
    public static IEndpointRouteBuilder MapStyleDirectorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/style-director")
            .WithTags("StyleDirector")
            .RequireAuthorization()
            .RequireAntiforgeryValidation();

        group.MapPost("/run", async (
            StyleDirectorRequestDto request,
            StyleDirectorWorkflow workflow,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("StyleDirectorEndpoints");

            if (string.IsNullOrWhiteSpace(request.ImageData))
                return Results.BadRequest("ImageData is required.");

            ImageBytes imageBytes;
            try { imageBytes = ImageBytes.FromBase64(request.ImageData, request.ContentType); }
            catch (ImageValidationException ex) { return Results.BadRequest(ex.Message); }
            var imageBytesArray = imageBytes.Bytes.ToArray();

            var input = new VisionAnalystInput(
                ImageBytes: imageBytesArray,
                DetectedTags: request.DetectedTags ?? [],
                ConfidenceScore: request.ConfidenceScore);

            try
            {
                var result = await workflow.RunAsync(input, ct);
                logger.LogInformation("Style director run. Succeeded={Success}, Elapsed={Elapsed}ms",
                    result.Succeeded, result.ElapsedMs);
                return Results.Ok(MapToDto(result));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Style director workflow crashed");
                return Results.Problem("Style director workflow crashed.", statusCode: 500, title: "Workflow Error");
            }
        })
        .WithName("RunStyleDirector")
        .WithSummary("Run the 4-agent Style Director workflow (Idea #1)");

        return app;
    }

    /// <summary>
    /// Maps the Application-layer <see cref="WorkflowResult{TOut}"/> into the wire DTO.
    /// Lives in the Web project because Shared cannot reference Application.
    /// </summary>
    private static StyleDirectorResultDto MapToDto(WorkflowResult<StyleDirectorResult> w) => w.Succeeded
        ? new StyleDirectorResultDto(
            Succeeded: true,
            ErrorMessage: null,
            FinalPrompt: w.Output.Decision.FinalPrompt,
            Critique: w.Output.Decision.Critique,
            Confidence: w.Output.Decision.Confidence,
            Subject: w.Output.Vision.Subject,
            Mood: w.Output.Vision.Mood,
            Themes: w.Output.Vision.Themes,
            Directions: w.Output.Strategy.Directions.Select(MapDirection).ToList(),
            StrategyRationale: w.Output.Strategy.Rationale,
            WhyThisPrompt: w.Output.Refined.WhyThisPrompt,
            Suggestions: w.Output.Decision.Suggestions,
            ElapsedMs: w.ElapsedMs,
            Reasoning: w.Reasoning.Select(MapReasoning).ToList())
        : new StyleDirectorResultDto(
            Succeeded: false,
            ErrorMessage: w.ErrorMessage,
            FinalPrompt: null,
            Critique: null,
            Confidence: null,
            Subject: null,
            Mood: null,
            Themes: null,
            Directions: null,
            StrategyRationale: null,
            WhyThisPrompt: null,
            Suggestions: null,
            ElapsedMs: w.ElapsedMs,
            Reasoning: w.Reasoning.Select(MapReasoning).ToList());

    private static StyleDirectorReasoningEntryDto MapReasoning(AgentReasoningEntry e) => new(
        e.AgentId, e.AgentDisplayName, e.IconClass, e.Summary,
        e.ElapsedMs, e.TokensUsed, e.Timestamp);

    private static StyleDirectionDto MapDirection(StyleDirection d) =>
        new(d.Name, d.Palette, d.Technique, d.ReferenceEra);
}
