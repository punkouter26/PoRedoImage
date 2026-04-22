using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Shared.DTOs;

namespace PoRedoImage.Application.Services;

/// <summary>
/// Orchestrates the image analysis pipeline (Analyze → Enhance → Generate).
/// Single Responsibility Principle (SOLID-S): coordinates domain services without knowing their implementations.
/// Open/Closed Principle (SOLID-O): new modes can be added without changing existing pipeline logic.
/// </summary>
public interface IImageAnalysisOrchestrator
{
    Task<ImageAnalysisResponse> ProcessAsync(ImageAnalysisRequest request, CancellationToken ct = default);
}

public sealed class ImageAnalysisOrchestrator(
    IVisionService visionService,
    IGenerativeAiService aiService,
    IMemeGeneratorService memeService,
    IImagen3Service imagen3Service,
    ILogger<ImageAnalysisOrchestrator> logger) : IImageAnalysisOrchestrator
{
    public async Task<ImageAnalysisResponse> ProcessAsync(ImageAnalysisRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.LogInformation("Starting image analysis pipeline. Mode={Mode}", request.Mode);

        var imageBytes = Convert.FromBase64String(request.ImageData);
        var metrics = new ProcessingMetricsDto();
        var response = new ImageAnalysisResponse();

        // Step 1 — Vision analysis (always runs)
        var (description, tags, confidence, analysisMs) = await visionService.AnalyzeAsync(imageBytes, ct);
        metrics.ImageAnalysisTimeMs = analysisMs;
        response.Tags = [.. tags];
        response.ConfidenceScore = confidence;

        if (request.Mode == ProcessingMode.MemeGeneration)
        {
            // Meme branch: generate caption + overlay
            var (top, bottom, memeTokens, memeMs) = await aiService.GenerateMemeCaptionAsync(tags, ct);
            metrics.DescriptionGenerationTimeMs = memeMs;
            metrics.DescriptionTokensUsed = memeTokens;

            var (memeData, memeType) = await memeService.GenerateMemeAsync(imageBytes, top, bottom, ct);
            response.MemeImageData = Convert.ToBase64String(memeData);
            response.MemeCaption = $"{top} / {bottom}";
            response.RegeneratedImageContentType = memeType;
        }
        else
        {
            // ImageRegeneration branch: enhance description → DALL-E or Imagen3
            var (enhanced, tokens, enhanceMs) = await aiService.EnhanceDescriptionAsync(
                description, tags, request.DescriptionLength, ct);
            metrics.DescriptionGenerationTimeMs = enhanceMs;
            metrics.DescriptionTokensUsed = tokens;
            response.Description = enhanced;

            byte[] imgData;
            string imgType;
            long regenMs;

            if (imagen3Service.IsConfigured)
            {
                (imgData, imgType, regenMs) = await imagen3Service.GenerateAsync(enhanced, ct);
            }
            else
            {
                (imgData, imgType, regenMs) = await aiService.GenerateImageAsync(enhanced, ct);
            }

            metrics.ImageRegenerationTimeMs = regenMs;
            response.RegeneratedImageData = Convert.ToBase64String(imgData);
            response.RegeneratedImageContentType = imgType;
        }

        response.Metrics = metrics;
        logger.LogInformation("Image analysis pipeline complete. TotalMs={Total}", metrics.TotalProcessingTimeMs);
        return response;
    }
}
