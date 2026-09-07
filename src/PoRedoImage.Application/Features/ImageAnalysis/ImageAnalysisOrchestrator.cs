using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Shared.DTOs;

namespace PoRedoImage.Application.Features.ImageAnalysis;

public sealed class ImageAnalysisOrchestrator(
    IVisionServiceRouter visionRouter,
    IGenerativeAiService aiService,
    IMemeGeneratorService memeService,
    IImageGenerationRouter imageGenRouter,
    ReproductionPromptWriter reproductionPromptWriter,
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
            // ImageRegeneration branch: write the prompt → Gemini text-to-image.
            //
            // Text-to-image is the deliberate design here: the photo itself is never sent to the
            // generator, so the prompt is the only thing carrying it across and its detail is the
            // entire ceiling on how close the result can land. That is why the normal path is a
            // vision pass over the real pixels (ReproductionPromptWriter) rather than
            // EnhanceDescriptionAsync, which only ever saw `description` — usually Computer Vision's
            // tag-join, since its Caption feature is region-unavailable here — and rewrote that
            // keyword list into a vivid scene of its own invention.
            string enhanced;
            string? promptFallbackReason = null;

            if (!string.IsNullOrWhiteSpace(request.PrecomputedEnhancedPrompt))
            {
                // On-device prompt: re-running it would bill a metered API for work already done free.
                enhanced = request.PrecomputedEnhancedPrompt;
                metrics.DescriptionGenerationTimeMs = 0;
                metrics.DescriptionTokensUsed = 0;
                logger.LogInformation("Using the client's on-device prompt; skipped the enhancement call.");
            }
            else if (!string.IsNullOrWhiteSpace(request.PrecomputedDescription))
            {
                // The user picked a browser-local vision model, so the photo has already been looked
                // at once, for free. Sending it to a metered vision model anyway would be the
                // surprise charge that browser-local execution exists to avoid — so this path keeps
                // the cheap text rewrite and tells the user why the result is looser.
                var (enhancedText, tokens, enhanceMs) = await aiService.EnhanceDescriptionAsync(
                    description, tags, request.DescriptionLength, ct);
                metrics.DescriptionGenerationTimeMs = enhanceMs;
                metrics.DescriptionTokensUsed = tokens;
                enhanced = enhancedText;
                promptFallbackReason =
                    "The prompt was built from your on-device model's description rather than a "
                    + "detailed read of the photo, so the new image will follow it loosely. Pick a "
                    + "remote vision model for a closer match.";
            }
            else
            {
                var repro = await reproductionPromptWriter.WriteAsync(
                    imageBytes, description, tags, request.DescriptionLength, ct);

                if (repro.Text is not null)
                {
                    metrics.DescriptionGenerationTimeMs = repro.ElapsedMs;
                    metrics.DescriptionTokensUsed = repro.TokensUsed;
                    enhanced = repro.Text;
                }
                else
                {
                    // No vision model, or the call failed. The tag-derived rewrite is all that is
                    // left; repro.FallbackReason says which, and why the result will not match.
                    var (enhancedText, tokens, enhanceMs) = await aiService.EnhanceDescriptionAsync(
                        description, tags, request.DescriptionLength, ct);
                    metrics.DescriptionGenerationTimeMs = repro.ElapsedMs + enhanceMs;
                    metrics.DescriptionTokensUsed = tokens;
                    enhanced = enhancedText;
                    promptFallbackReason = repro.FallbackReason;
                }
            }

            response.Description = enhanced;

            // The vision step may already have reported its own degradation. Both matter and they
            // have different causes, so neither is allowed to overwrite the other.
            response.DescriptionFallbackReason = JoinReasons(visionFallbackReason, promptFallbackReason);

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

    /// <summary>
    /// Combines the degradation notices from the vision step and the prompt step into the single
    /// field the client renders, dropping the ones that did not fire.
    /// </summary>
    private static string? JoinReasons(params string?[] reasons)
    {
        var present = reasons.Where(r => !string.IsNullOrWhiteSpace(r)).ToArray();
        return present.Length == 0 ? null : string.Join(" ", present);
    }
}
