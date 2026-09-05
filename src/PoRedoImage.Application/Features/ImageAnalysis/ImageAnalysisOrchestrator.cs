using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Shared.DTOs;

namespace PoRedoImage.Application.Features.ImageAnalysis;

public sealed class ImageAnalysisOrchestrator(
    IVisionServiceRouter visionRouter,
    IGenerativeAiService aiService,
    IMemeGeneratorService memeService,
    IImageGenerationRouter imageGenRouter,
    ILogger<ImageAnalysisOrchestrator> logger) : IImageAnalysisOrchestrator
{
    public async Task<ImageAnalysisResponse> ProcessAsync(ImageAnalysisRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        logger.PipelineStarting(request.Mode);

        var imageBytes = Convert.FromBase64String(request.ImageData);
        var metrics = new ProcessingMetricsDto();
        var response = new ImageAnalysisResponse();

        // Step 1 — Vision analysis. Skipped entirely when the client ran a browser-local model and
        // supplied the result: re-running it would bill a metered API for work already done for free.
        string description;
        IReadOnlyList<string> tags;
        double confidence;
        string? visionFallbackReason = null;

        if (!string.IsNullOrWhiteSpace(request.PrecomputedDescription))
        {
            description = request.PrecomputedDescription;
            tags = request.PrecomputedTags ?? [];
            // Local models emit no calibrated confidence; report 1.0 so downstream gating treats the
            // result as usable, matching how OllamaVisionService already handles this.
            confidence = 1.0;
            metrics.ImageAnalysisTimeMs = 0;
        }
        else
        {
            var visionService = visionRouter.Resolve(request.ModelId);
            var (visionDescription, visionTags, visionConfidence, analysisMs, visionFallback) =
                await visionService.AnalyzeAsync(imageBytes, ct);
            description = visionDescription;
            tags = visionTags;
            confidence = visionConfidence;
            metrics.ImageAnalysisTimeMs = analysisMs;
            visionFallbackReason = visionFallback;
        }

        response.Tags = [.. tags];
        response.ConfidenceScore = confidence;
        response.DescriptionFallbackReason = visionFallbackReason;

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
            // ImageRegeneration branch: enhance description → Gemini image generation.
            // Skipped entirely when the client produced the prompt on-device, exactly as the vision
            // step above is — re-running it would bill a metered API for work already done free.
            string enhanced;
            if (!string.IsNullOrWhiteSpace(request.PrecomputedEnhancedPrompt))
            {
                enhanced = request.PrecomputedEnhancedPrompt;
                metrics.DescriptionGenerationTimeMs = 0;
                metrics.DescriptionTokensUsed = 0;
                logger.LogInformation("Using the client's on-device prompt; skipped the enhancement call.");
            }
            else
            {
                var (enhancedText, tokens, enhanceMs) = await aiService.EnhanceDescriptionAsync(
                    description, tags, request.DescriptionLength, ct);
                metrics.DescriptionGenerationTimeMs = enhanceMs;
                metrics.DescriptionTokensUsed = tokens;
                enhanced = enhancedText;
            }

            response.Description = enhanced;

            var imageGenService = imageGenRouter.Resolve(request.ImageGenModelId);

            if (!imageGenService.IsConfigured)
            {
                throw new InvalidOperationException(
                    "Image generation is not configured. Set the Gemini API key (Google:ApiKey) via Key Vault or appsettings.");
            }

            var (imgData, imgType, regenMs) = await imageGenService.GenerateAsync(enhanced, ct);

            metrics.ImageRegenerationTimeMs = regenMs;
            response.RegeneratedImageData = Convert.ToBase64String(imgData);
            response.RegeneratedImageContentType = imgType;
        }

        response.Metrics = metrics;
        logger.PipelineComplete(metrics.TotalProcessingTimeMs);
        return response;
    }
}
