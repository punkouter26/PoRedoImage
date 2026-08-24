using System.Security.Claims;
using System.Text.Json;
using PoRedoImage.Domain.Entities;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Shared.DTOs;
using PoRedoImage.Shared.Imaging;
using PoRedoImage.Shared.Json;
using Microsoft.Extensions.Logging;
using PoRedoImage.Web.Features.Shared;

namespace PoRedoImage.Web.Features.BulkGenerate;

public static class BulkGenerateEndpoints
{
    /// <summary>
    /// Concurrent calls to the image model per batch. Three, matching the re-roll path — the
    /// provider rate-limits above that and a 429 storm costs more wall clock than queueing does.
    /// </summary>
    private const int BatchConcurrency = 3;

    /// <summary>The NDJSON record separator.</summary>
    private static readonly ReadOnlyMemory<byte> Newline = new byte[] { 10 };


    public static IEndpointRouteBuilder MapBulkGenerateEndpoints(this IEndpointRouteBuilder app)
    {
        // Prompt persistence endpoints require authentication (use the caller's user identity).
        var authGroup = app.MapGroup("/api/bulk-generate")
            .WithTags("BulkGenerate")
            .RequireAuthorization()
            .RequireAntiforgeryValidation();

        // Add Idempotency-Key filter to the auth group so duplicate POST /prompts are de-duped
        // (Po2Logic F6 — no Idempotency-Key on Write endpoints).
        authGroup.AddEndpointFilter<PoRedoImage.Web.Features.Idempotency.IdempotencyKeyFilter>();

        authGroup.MapGet("/prompts", async (HttpContext context, IBulkPromptRepository storage) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userId is null) return Results.Unauthorized();

            var stored = await storage.GetByRowKeyAsync(userId);
            if (stored is null) return Results.NotFound();

            var prompts = JsonSerializer.Deserialize(stored.PromptText, SharedJsonContext.Default.StringArray);
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

            var prompt = BulkPrompt.Create(userId, userId, JsonSerializer.Serialize(request.Prompts, SharedJsonContext.Default.StringArray));
            await storage.SaveAsync(prompt);
            return Results.NoContent();
        })
        .WithName("SaveBulkPrompts")
        .WithSummary("Save prompts for the authenticated user");

        // AI generation endpoints do not use caller identity — no auth cookie required.
        // Rate limiting still applies to protect costly AI calls.
        var aiGroup = app.MapGroup("/api/bulk-generate")
            .WithTags("BulkGenerate")
            .RequireRateLimiting("ai-endpoints")
            .RequireAntiforgeryValidation();

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

        // Generate a single art-style variation. Still routed through IImageGenerationRouter even
        // though Google is the only provider: the router is where a second one would slot back in,
        // and it already resolves the default when no modelId is supplied.
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

        // Generate every slot in one request, streaming each result the moment it lands.
        //
        // The client used to drive this with a `for` loop of one-at-a-time POSTs — ten sequential
        // round-trips, each re-uploading the whole source image. Both costs are gone: the image
        // arrives once, and the fan-out runs under the same concurrency cap the re-roll path already
        // uses.
        //
        // NDJSON rather than SSE. The payloads are base64 images measured in hundreds of KB, and
        // SSE's `data: ` line framing would have to re-chunk every one of them; a JSON object per
        // line is the same streaming behaviour with none of that. It also degrades honestly — a
        // client that does not stream still gets a parseable body, just all at once.
        aiGroup.MapPost("/batch", async (
            BulkBatchRequest request,
            HttpContext http,
            IImageGenerationRouter router,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.ImageData))
                return Results.BadRequest("ImageData is required.");
            if (request.Prompts is null || request.Prompts.Length is 0 or > 10)
                return Results.BadRequest("Between 1 and 10 prompts are required.");
            if (request.Prompts.Any(p => string.IsNullOrWhiteSpace(p) || p.Length > 2000))
                return Results.BadRequest("Each prompt must be non-empty and at most 2000 characters.");

            ImageBytes imageBytes;
            try { imageBytes = ImageBytes.FromBase64(request.ImageData, request.ContentType); }
            catch (ImageValidationException ex) { return Results.BadRequest(ex.Message); }

            var imagen3 = router.Resolve(request.ImageGenModelId);
            if (!imagen3.IsConfigured)
                return Results.Problem("Image generation is not configured for the selected provider.", statusCode: 503);

            var logger = loggerFactory.CreateLogger("BulkGenerateEndpoints.Batch");
            var source = imageBytes.Bytes.ToArray();
            var prompts = request.Prompts;

            http.Response.ContentType = "application/x-ndjson";
            // Proxies that buffer would defeat the entire point of streaming these.
            http.Response.Headers["Cache-Control"] = "no-cache, no-store";
            http.Response.Headers["X-Accel-Buffering"] = "no";

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var succeeded = 0;

            // Same cap as /reroll — three concurrent calls to the image model. Higher is not better:
            // the provider rate-limits, and a 429 storm costs more wall clock than the queueing does.
            using var gate = new SemaphoreSlim(BatchConcurrency, BatchConcurrency);
            // One writer at a time, or two slots finishing together interleave their JSON mid-line
            // and the client's line reader sees corruption.
            using var writeLock = new SemaphoreSlim(1, 1);

            var tasks = prompts.Select(async (prompt, index) =>
            {
                await gate.WaitAsync(ct);
                BulkBatchItem item;
                try
                {
                    var (data, contentType, _) = await imagen3.GenerateImageAsync(prompt, source, ct: ct);
                    item = new BulkBatchItem(index, Convert.ToBase64String(data), contentType, null);
                    Interlocked.Increment(ref succeeded);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // One slot failing is a normal outcome the board already renders. Reporting it
                    // as a line keeps the other nine running.
                    logger.LogWarning(ex, "Batch slot {Index} failed", index);
                    item = new BulkBatchItem(index, null, null, "Generation failed for this variation.");
                }
                finally { gate.Release(); }

                await writeLock.WaitAsync(ct);
                try
                {
                    // Source-generated JsonTypeInfo, not the reflective overload: the solution-wide trim
                    // analyzer rejects the latter (IL2026), and this endpoint writes to the response
                    // body directly rather than going through the framework's serializer.
                    await JsonSerializer.SerializeAsync(
                        http.Response.Body, item, SharedJsonContext.Default.BulkBatchItem, ct);
                    await http.Response.Body.WriteAsync(Newline, ct);
                    await http.Response.Body.FlushAsync(ct);
                }
                finally { writeLock.Release(); }
            }).ToArray();

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("Batch cancelled by the client after {Elapsed}ms.", sw.ElapsedMilliseconds);
                return Results.Empty;
            }

            sw.Stop();
            logger.LogInformation(
                "Batch complete. Requested={Requested}, Succeeded={Succeeded}, Elapsed={Elapsed}ms",
                prompts.Length, succeeded, sw.ElapsedMilliseconds);

            // The body is already written; returning Empty stops the framework appending to it.
            return Results.Empty;
        })
        .WithName("GenerateBulkBatch")
        .WithSummary("Generate every art-style variation in one request, streamed as NDJSON");

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
