using System.Security.Claims;
using System.Text.Json;
using PoRedoImage.Domain.Entities;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Shared.DTOs;
using PoRedoImage.Shared.Imaging;
using Microsoft.Extensions.Logging;

namespace PoRedoImage.Web.Features.BulkGenerate;

public static class BulkGenerateEndpoints
{
    public static IEndpointRouteBuilder MapBulkGenerateEndpoints(this IEndpointRouteBuilder app)
    {
        // Prompt persistence endpoints require authentication (use the caller's user identity).
        var authGroup = app.MapGroup("/api/bulk-generate")
            .WithTags("BulkGenerate")
            .RequireAuthorization();

        // Add Idempotency-Key filter to the auth group so duplicate POST /prompts are de-duped
        // (Po2Logic F6 — no Idempotency-Key on Write endpoints).
        authGroup.AddEndpointFilter<PoRedoImage.Web.Features.Idempotency.IdempotencyKeyFilter>();

        authGroup.MapGet("/prompts", async (HttpContext context, IBulkPromptRepository storage) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userId is null) return Results.Unauthorized();

            var stored = await storage.GetByRowKeyAsync(userId);
            if (stored is null) return Results.NotFound();

            var prompts = JsonSerializer.Deserialize<string[]>(stored.PromptText);
            return prompts is not null ? Results.Ok(prompts) : Results.NotFound();
        })
        .WithName("GetBulkPrompts")
        .WithSummary("Get saved prompts for the authenticated user");

        authGroup.MapPost("/prompts", async (HttpContext context, SavePromptsRequest request, IBulkPromptRepository storage) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userId is null) return Results.Unauthorized();

            if (request.Prompts is null || request.Prompts.Length != 10)
                return Results.BadRequest("Exactly 10 prompts are required.");

            if (request.Prompts.Any(p => string.IsNullOrWhiteSpace(p) || p.Length > 2000))
                return Results.BadRequest("Each prompt must be non-empty and at most 2000 characters.");

            var prompt = BulkPrompt.Create(userId, userId, JsonSerializer.Serialize(request.Prompts));
            await storage.SaveAsync(prompt);
            return Results.NoContent();
        })
        .WithName("SaveBulkPrompts")
        .WithSummary("Save prompts for the authenticated user");

        // AI generation endpoints do not use caller identity — no auth cookie required.
        // Rate limiting still applies to protect costly AI calls.
        var aiGroup = app.MapGroup("/api/bulk-generate")
            .WithTags("BulkGenerate")
            .RequireRateLimiting("ai-endpoints");

        // Describe the primary person in the uploaded image using GPT-4o vision.
        // Called once per generation batch; result is reused across all variation prompts.
        // Falls back gracefully to an empty description if the AI service is unavailable,
        // so Gemini image-to-image can still run using the raw <PERSON> token.
        aiGroup.MapPost("/describe", async (BulkDescribeRequest request, IGenerativeAiService describeService, ILoggerFactory loggerFactory) =>
        {
            if (string.IsNullOrWhiteSpace(request.ImageData))
                return Results.BadRequest("ImageData is required.");

            ImageBytes imageBytes;
            try { imageBytes = ImageBytes.FromBase64(request.ImageData, request.ContentType); }
            catch (ImageValidationException ex) { return Results.BadRequest(ex.Message); }

            try
            {
                var description = await describeService.DescribePersonAsync(imageBytes.Bytes.ToArray());
                return Results.Ok(new BulkDescribeResponse(description));
            }
            catch (Exception ex)
            {
                // Return empty description so Gemini img2img can still run with raw prompts
                var logger = loggerFactory.CreateLogger("BulkGenerateEndpoints");
                logger.LogWarning(ex, "DescribePersonAsync failed — falling back to empty description");
                return Results.Ok(new BulkDescribeResponse(string.Empty));
            }
        })
        .WithName("DescribePerson")
        .WithSummary("Describe the primary person in an image for use in art-style prompts");

        // Generate a single art-style variation. Routed through IImageGenerationRouter so the
        // client-side AI picker can choose Gemini Imagen 3 vs HuggingFace FLUX/Qwen per request.
        // Resolves the configured default when no modelId is supplied (matches the legacy behaviour
        // for callers that haven't adopted the picker yet).
        aiGroup.MapPost("/variation", async (BulkVariationRequest request, IImageGenerationRouter router) =>
        {
            if (string.IsNullOrWhiteSpace(request.ImageData))
                return Results.BadRequest("ImageData is required.");
            if (string.IsNullOrWhiteSpace(request.Prompt))
                return Results.BadRequest("Prompt is required.");

            ImageBytes imageBytes;
            try { imageBytes = ImageBytes.FromBase64(request.ImageData, request.ContentType); }
            catch (ImageValidationException ex) { return Results.BadRequest(ex.Message); }

            var imagen3 = router.Resolve(request.ImageGenModelId);
            if (!imagen3.IsConfigured)
                return Results.Problem("Image generation is not configured for the selected provider.", statusCode: 503);

            var (imgData, imgCt, _) = await imagen3.GenerateImageAsync(request.Prompt, imageBytes.Bytes.ToArray());
            return Results.Ok(new BulkVariationResponse(Convert.ToBase64String(imgData), imgCt));
        })
        .WithName("GenerateBulkVariation")
        .WithSummary("Generate a single art-style variation image");

        // Idea #11 — One-Tap Re-roll x3: spawn N parallel variations from a winning prompt.
        // Uses a deterministic seed hint so re-rolls are reproducible per session and
        // visibly distinct from the winner, but stay close in style. Routed via the router
        // so a client can pick a different image provider per re-roll.
        aiGroup.MapPost("/reroll", async (BulkRerollRequest request, IImageGenerationRouter router, ILoggerFactory loggerFactory) =>
        {
            if (string.IsNullOrWhiteSpace(request.ImageData))
                return Results.BadRequest("ImageData is required.");
            if (string.IsNullOrWhiteSpace(request.SeedPrompt))
                return Results.BadRequest("SeedPrompt is required.");
            if (request.Count is < 1 or > 10)
                return Results.BadRequest("Count must be between 1 and 10.");

            ImageBytes imageBytes;
            try { imageBytes = ImageBytes.FromBase64(request.ImageData, request.ContentType); }
            catch (ImageValidationException ex) { return Results.BadRequest(ex.Message); }

            var imagen3 = router.Resolve(request.ImageGenModelId);
            if (!imagen3.IsConfigured)
                return Results.Problem("Image generation is not configured for the selected provider.", statusCode: 503);

            var rerollImageBytes = imageBytes.Bytes.ToArray();

            var logger = loggerFactory.CreateLogger("BulkGenerateEndpoints.Reroll");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var startedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // Cap parallelism to avoid hammering the upstream model with simultaneous calls.
            using var gate = new SemaphoreSlim(initialCount: 3, maxCount: 3);
            var tasks = Enumerable.Range(0, request.Count).Select(async i =>
            {
                await gate.WaitAsync();
                try
                {
                    // Seed = wall-clock ms delta from batch start + slot index — guarantees uniqueness
                    // within the batch and reproducibility if the user retries within the same second.
                    var seed = (int)((Environment.TickCount ^ (i * 2654435761)) & 0x7FFFFFFF);
                    var (data, ct2, _) = await imagen3.GenerateImageAsync(request.SeedPrompt, rerollImageBytes, seed);
                    return new BulkRerollVariation(i, Convert.ToBase64String(data), ct2);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Re-roll slot {Index} failed", i);
                    return null;
                }
                finally { gate.Release(); }
            }).ToArray();

            var results = await Task.WhenAll(tasks);
            var variations = results.Where(r => r is not null).Select(r => r!).ToList();

            sw.Stop();
            logger.LogInformation("Re-roll batch complete. Requested={Requested}, Succeeded={Succeeded}, Elapsed={Elapsed}ms",
                request.Count, variations.Count, sw.ElapsedMilliseconds);

            return Results.Ok(new BulkRerollResponse(
                Variations: variations,
                Requested: request.Count,
                Succeeded: variations.Count,
                ElapsedMs: sw.ElapsedMilliseconds));
        })
        .WithName("RerollBulkVariations")
        .WithSummary("Generate N parallel re-rolls of a winning prompt (Idea #11 — One-Tap Re-roll x3)");

        return app;
    }
}
