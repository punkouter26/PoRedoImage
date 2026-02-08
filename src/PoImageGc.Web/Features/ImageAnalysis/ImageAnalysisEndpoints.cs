using Microsoft.AspNetCore.Mvc;
using PoImageGc.Web.Models;

namespace PoImageGc.Web.Features.ImageAnalysis;

/// <summary>
/// Minimal API endpoints for image analysis feature
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
            .Produces<ImageAnalysisResult>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

        group.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Service = "ImageAnalysis" }))
            .WithName("ImageAnalysisHealth")
            .WithSummary("Check image analysis service health");
    }

    private static async Task<IResult> AnalyzeImageAsync(
        [FromBody] ImageAnalysisRequest request,
        IComputerVisionService computerVisionService,
        IOpenAIService openAIService,
        IMemeGeneratorService memeGeneratorService,
        ILogger<ImageAnalysisRequest> logger)
    {
        if (string.IsNullOrEmpty(request.ImageData))
        {
            return Results.Problem(
                detail: "Image data is required",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Validation Error");
        }

        try
        {
            logger.LogInformation("Processing image analysis request. Mode: {Mode}", request.Mode);

            // Convert base64 to bytes
            var imageBytes = Convert.FromBase64String(request.ImageData);

            // Step 1: Analyze image with Computer Vision
            var (description, tags, confidence, analysisTime) = await computerVisionService.AnalyzeImageAsync(imageBytes);

            var result = new ImageAnalysisResult
            {
                Description = description,
                Tags = tags,
                ConfidenceScore = confidence,
                Metrics = new ProcessingMetrics
                {
                    ImageAnalysisTimeMs = analysisTime
                }
            };

            // Step 2: Process based on mode
            if (request.Mode == ProcessingMode.MemeGeneration)
            {
                // Generate meme caption
                var (topText, bottomText, memeTokens, memeTime) = await openAIService.GenerateMemeCaptionAsync(tags, confidence);
                result.MemeCaption = $"{topText}\n{bottomText}";
                result.Metrics.DescriptionTokensUsed = memeTokens;
                result.Metrics.DescriptionGenerationTimeMs = memeTime;

                // Generate meme image
                var memeImageBytes = memeGeneratorService.AddCaptionToImage(imageBytes, topText, bottomText);
                result.MemeImageData = Convert.ToBase64String(memeImageBytes);
            }
            else // ImageRegeneration mode
            {
                // Enhance description
                var (enhancedDesc, descTokens, descTime) = await openAIService.EnhanceDescriptionAsync(
                    description, tags, request.DescriptionLength);
                result.Description = enhancedDesc;
                result.Metrics.DescriptionGenerationTimeMs = descTime;
                result.Metrics.DescriptionTokensUsed = descTokens;

                // Generate new image with DALL-E
                var (generatedImage, contentType, _, regenTime) = await openAIService.GenerateImageAsync(enhancedDesc);
                result.RegeneratedImageData = Convert.ToBase64String(generatedImage);
                result.RegeneratedImageContentType = contentType;
                result.Metrics.ImageRegenerationTimeMs = regenTime;
            }

            logger.LogInformation("Image analysis completed. Total time: {TotalTime}ms", result.Metrics.TotalProcessingTimeMs);

            return Results.Ok(result);
        }
        catch (FormatException ex)
        {
            logger.LogWarning(ex, "Invalid base64 image data");
            return Results.Problem(
                detail: "Invalid base64 image data",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid Input");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing image analysis request");
            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Processing Error");
        }
    }
}
