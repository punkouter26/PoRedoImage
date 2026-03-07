using System.Security.Claims;

namespace PoRedoImage.Web.Features.BulkGenerate;

public static class BulkGenerateEndpoints
{
    public static IEndpointRouteBuilder MapBulkGenerateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/bulk-generate")
            .WithTags("BulkGenerate")
            .RequireAuthorization();

        group.MapGet("/prompts", async (HttpContext context, IBulkPromptStorageService storage) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userId is null) return Results.Unauthorized();

            var prompts = await storage.LoadPromptsAsync(userId);
            return prompts is not null ? Results.Ok(prompts) : Results.NotFound();
        })
        .WithName("GetBulkPrompts")
        .WithSummary("Get saved prompts for the authenticated user");

        group.MapPost("/prompts", async (HttpContext context, SavePromptsRequest request, IBulkPromptStorageService storage) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userId is null) return Results.Unauthorized();

            if (request.Prompts is null || request.Prompts.Length != 10)
                return Results.BadRequest("Exactly 10 prompts are required.");

            if (request.Prompts.Any(p => string.IsNullOrWhiteSpace(p) || p.Length > 2000))
                return Results.BadRequest("Each prompt must be non-empty and at most 2000 characters.");

            await storage.SavePromptsAsync(userId, request.Prompts);
            return Results.NoContent();
        })
        .WithName("SaveBulkPrompts")
        .WithSummary("Save prompts for the authenticated user");

        return app;
    }
}

public record SavePromptsRequest(string[] Prompts);
