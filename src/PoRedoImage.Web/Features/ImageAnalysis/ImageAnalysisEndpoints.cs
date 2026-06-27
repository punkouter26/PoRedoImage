using Microsoft.AspNetCore.Mvc;
using PoRedoImage.Application.Features.ImageAnalysis;
using PoRedoImage.Shared.DTOs;
using PoRedoImage.Shared.Imaging;
using PoRedoImage.Web.Features;
using System.ClientModel;
using Azure;

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
            .WithTags("Image Analysis");

        group.MapPost("/analyze", AnalyzeImageAsync)
            .WithName("AnalyzeImage")
            .WithSummary("Analyze an image and optionally generate content")
            .Produces<ImageAnalysisResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError)
            .RequireAuthorization()
            .RequireRateLimiting("ai-endpoints")
            .AddEndpointFilter<ValidationFilter<ImageAnalysisRequest>>();

        group.MapGet("/health", (IConfiguration config) =>
        {
            var cvHealthy = !string.IsNullOrEmpty(config["ComputerVision:Endpoint"])
                            && !string.IsNullOrEmpty(config["ComputerVision:ApiKey"]);
            var oaiHealthy = !string.IsNullOrEmpty(config["OpenAI:Endpoint"])
                             && !string.IsNullOrEmpty(config["OpenAI:Key"]);
            var status = cvHealthy && oaiHealthy ? "Healthy" : "Degraded";
            return Results.Ok(new
            {
                status,
                services = new
                {
                    computerVision = cvHealthy ? "Healthy" : "Degraded",
                    openAI = oaiHealthy ? "Healthy" : "Degraded"
                }
            });
        })
        .WithName("ImageAnalysisHealth")
        .WithSummary("Configuration readiness check for AI image services")
        .Produces(StatusCodes.Status200OK);
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
        catch (ClientResultException ex) when (ex.Message.Contains("content_policy_violation"))
        {
            logger.LogWarning(ex, "Image generation blocked by content policy");
            return Results.Problem(
                detail: "The image was blocked by content safety filters. Please try a different image.",
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
}


