using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Shared.DTOs;

namespace PoRedoImage.Application.Features.ImageAnalysis;

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
            // ImageRegeneration branch: enhance description → Gemini image generation
            var (enhanced, tokens, enhanceMs) = await aiService.EnhanceDescriptionAsync(
                description, tags, request.DescriptionLength, ct);
            metrics.DescriptionGenerationTimeMs = enhanceMs;
            metrics.DescriptionTokensUsed = tokens;
            response.Description = enhanced;

            if (!imagen3Service.IsConfigured)
            {
                throw new InvalidOperationException(
                    "Image generation is not configured. Set the Gemini API key (Google:ApiKey) via Key Vault or appsettings.");
            }

            var (imgData, imgType, regenMs) = await imagen3Service.GenerateAsync(enhanced, ct);

            metrics.ImageRegenerationTimeMs = regenMs;
            response.RegeneratedImageData = Convert.ToBase64String(imgData);
            response.RegeneratedImageContentType = imgType;
        }

        response.Metrics = metrics;
        logger.LogInformation("Image analysis pipeline complete. TotalMs={Total}", metrics.TotalProcessingTimeMs);
        return response;
    }
}
