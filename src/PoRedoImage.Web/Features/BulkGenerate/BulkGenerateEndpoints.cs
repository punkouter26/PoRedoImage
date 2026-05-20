using System.Security.Claims;
using System.Text.Json;
using PoRedoImage.Domain.Entities;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Shared.DTOs;
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

            byte[] imageBytes;
            try { imageBytes = Convert.FromBase64String(request.ImageData); }
            catch { return Results.BadRequest("ImageData must be valid base64."); }

            try
            {
                var description = await describeService.DescribePersonAsync(imageBytes);
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

        // Generate a single art-style variation using Gemini Imagen3 image-to-image.
        aiGroup.MapPost("/variation", async (BulkVariationRequest request, IImagen3Service imagen3) =>
        {
            if (string.IsNullOrWhiteSpace(request.ImageData))
                return Results.BadRequest("ImageData is required.");
            if (string.IsNullOrWhiteSpace(request.Prompt))
                return Results.BadRequest("Prompt is required.");
            if (!imagen3.IsConfigured)
                return Results.Problem("Gemini image generation is not configured.", statusCode: 503);

            byte[] imageBytes;
            try { imageBytes = Convert.FromBase64String(request.ImageData); }
            catch { return Results.BadRequest("ImageData must be valid base64."); }

            var (imgData, imgCt, _) = await imagen3.GenerateImageAsync(request.Prompt, imageBytes);
            return Results.Ok(new BulkVariationResponse(Convert.ToBase64String(imgData), imgCt));
        })
        .WithName("GenerateBulkVariation")
        .WithSummary("Generate a single art-style variation image");

        return app;
    }
}

public record SavePromptsRequest(string[] Prompts);
