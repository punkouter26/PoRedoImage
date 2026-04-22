using Microsoft.AspNetCore.Mvc;
using PoRedoImage.Application.Services;
using PoRedoImage.Shared.DTOs;
using PoRedoImage.Web.Features;
using System.ClientModel;

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
            .RequireRateLimiting("ai-endpoints")
            .AddEndpointFilter<ValidationFilter<ImageAnalysisRequest>>();

        group.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Service = "ImageAnalysis" }))
            .WithName("ImageAnalysisHealth")
            .WithSummary("Check image analysis service health");
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
            var imageBytes = Convert.FromBase64String(request.ImageData);
            if (!IsValidImageBytes(imageBytes))
                return Results.Problem(detail: "The uploaded file is not a valid JPEG or PNG image.", statusCode: 400, title: "Invalid Image");

            var result = await orchestrator.ProcessAsync(request, ct);
            return Results.Ok(result);
        }
        catch (FormatException ex)
        {
            logger.LogWarning(ex, "Invalid base64 image data");
            return Results.Problem(detail: "Invalid base64 image data", statusCode: 400, title: "Invalid Input");
        }
        catch (ClientResultException ex) when (ex.Message.Contains("content_policy_violation"))
        {
            logger.LogWarning(ex, "Image generation blocked by content policy");
            return Results.Problem(
                detail: "The image was blocked by content safety filters. Please try a different image.",
                statusCode: 422, title: "Content Policy Violation");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing image analysis request");
            return Results.Problem(detail: ex.Message, statusCode: 500, title: "Processing Error");
        }
    }

    /// <summary>Validates JPEG (FF D8 FF) or PNG (89 50 4E 47) magic bytes.</summary>
    private static bool IsValidImageBytes(byte[] bytes) =>
        (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) ||
        (bytes.Length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47);
}


