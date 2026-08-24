using Microsoft.AspNetCore.Mvc;
using PoRedoImage.Application.Features.ImageAnalysis;
using PoRedoImage.Shared.DTOs;
using PoRedoImage.Shared.Imaging;
using PoRedoImage.Web.Features.Shared;
using System.ClientModel;
using Azure;
using PoRedoImage.Shared.Configuration;

namespace PoRedoImage.Web.Features.ImageAnalysis;

/// <summary>
/// Minimal API endpoints for image analysis feature.
/// Thin slice: delegates all orchestration to IImageAnalysisOrchestrator (Application layer).
/// Open/Closed Principle (SOLID-O): new processing modes are added in the orchestrator, not here.
/// </summary>
public static class ImageAnalysisEndpoints
{
    public static void MapImageAnalysisEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/images")
            .WithTags("Image Analysis")
            .RequireAntiforgeryValidation();

        group.MapPost("/analyze", AnalyzeImageAsync)
            .WithName("AnalyzeImage")
            .WithSummary("Analyze an image and optionally generate content")
            .Produces<ImageAnalysisResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError)
            .RequireAuthorization()
            .RequireRateLimiting("ai-endpoints")
            .AddEndpointFilter<ValidationFilter<ImageAnalysisRequest>>();
    }

    private static async Task<IResult> AnalyzeImageAsync(
        [FromBody] ImageAnalysisRequest request,
        IImageAnalysisOrchestrator orchestrator,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("ImageAnalysisEndpoints");
        if (string.IsNullOrEmpty(request.ImageData))
            return Results.Problem(detail: "Image data is required", statusCode: 400, title: "Validation Error");

        if (request.DescriptionLength < 200 || request.DescriptionLength > 500)
            return Results.Problem(
                detail: $"DescriptionLength must be between 200 and 500. Provided: {request.DescriptionLength}",
                statusCode: 400, title: "Validation Error");

        try
        {
            var image = ImageBytes.FromBase64(request.ImageData, request.ContentType);
            var imageBytes = image.Bytes.ToArray();
            var result = await orchestrator.ProcessAsync(request, ct);
            return Results.Ok(result);
        }
        catch (ImageValidationException ex)
        {
            // Po2Logic F10 — magic-byte check now also accepts GIF, WebP, BMP; HEIC hint included.
            logger.LogWarning(ex, "Image validation failed: {Message}", ex.Message);
            return Results.Problem(detail: ex.Message, statusCode: 400, title: "Invalid Image");
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Gemini declined", StringComparison.OrdinalIgnoreCase))
        {
            // Surface the upstream model refusal as a 422 so the client can show a specific
            // "try a different image" message instead of a generic 500.
            logger.LogWarning(ex, "Gemini refused to generate an image");
            return Results.Problem(
                detail: "The image-generation model declined this request. Please try a different image or prompt.",
                statusCode: 422, title: "Generation Declined");
        }
        catch (ClientResultException ex) when (IsContentFiltered(ex.Message))
        {
            logger.LogWarning(ex, "AI request blocked by content safety filters");
            return Results.Problem(
                detail: "The AI declined this request — its content safety filters rejected either the "
                    + "image or the caption it would have had to write. Please try a different image.",
                statusCode: 422, title: "Content Policy Violation");
        }
        catch (ClientResultException ex) when (ex.Status == 401 || ex.Status == 403)
        {
            logger.LogWarning(ex, "AI service authentication failed (OpenAI) HTTP {Status}", ex.Status);
            return Results.Problem(
                detail: "AI service is not authorised — the API key or endpoint may be incorrect. Please check your configuration.",
                statusCode: 503, title: "Service Unavailable");
        }
        catch (RequestFailedException ex) when (ex.Status == 401 || ex.Status == 403)
        {
            logger.LogWarning(ex, "AI service authentication failed (Azure SDK) HTTP {Status}", ex.Status);
            return Results.Problem(
                detail: "AI service is not authorised — the API key or endpoint may be incorrect. Please check your configuration.",
                statusCode: 503, title: "Service Unavailable");
        }
        catch (RequestFailedException ex) when (ex.Status == 400)
        {
            logger.LogWarning(ex, "Azure AI service returned 400 Bad Request: {Error}", ex.ErrorCode);
            return Results.Problem(
                detail: $"The AI service rejected the request: {ex.ErrorCode ?? "InvalidRequest"}. This may be a region limitation — try a different image or contact support.",
                statusCode: 422, title: "AI Service Error");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing image analysis request");
            return Results.Problem(detail: "An error occurred while processing your image. Please try again.", statusCode: 500, title: "Processing Error");
        }
    }

    /// <summary>
    /// Whether an upstream chat/image failure is a content-safety refusal rather than a real fault.
    /// </summary>
    /// <remarks>
    /// The two vendors report the same condition under different codes, and this endpoint talks to
    /// both: <b>Azure</b> OpenAI returns <c>HTTP 400 (content_filter)</c> — the shape actually
    /// observed when the meme-caption prompt is rejected — while <b>OpenAI.com</b> returns
    /// <c>content_policy_violation</c>. Matching only the latter (the original guard clause) left
    /// every Azure refusal falling through to the catch-all, so the caller saw an opaque HTTP 500
    /// "An error occurred while processing your image" and had no idea a different photo would work.
    /// Matched on the exception message because neither SDK surfaces the code as a typed member.
    /// </remarks>
    internal static bool IsContentFiltered(string? message) =>
        message is not null
        && (message.Contains("content_filter", StringComparison.OrdinalIgnoreCase)
            || message.Contains("content_policy_violation", StringComparison.OrdinalIgnoreCase));
}


