using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Hybrid;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Shared.DTOs;
using PoRedoImage.Shared.Imaging;

namespace PoRedoImage.Web.Features.MemeTemplates;

/// <summary>
/// Minimal API endpoints for the Meme Template Library (Idea #17).
/// GET  /api/meme-templates            → list the curated catalog
/// GET  /api/meme-templates/{id}       → fetch a single template
/// POST /api/meme-templates/render     → render a user's photo with a template
/// </summary>
public static class MemeTemplateEndpoints
{
    public static IEndpointRouteBuilder MapMemeTemplateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/meme-templates")
            .WithTags("MemeTemplates")
            .RequireAuthorization();

        group.MapGet("/", async (IMemeTemplateService templates, HybridCache cache, CancellationToken ct) =>
        {
            // The catalog is immutable for the process lifetime, so cache the DTO projection rather
            // than re-projecting/serializing on every request. HybridCache gives L1 (+ L2 if a
            // distributed cache is later registered) with built-in stampede protection.
            var catalog = await cache.GetOrCreateAsync(
                "meme-templates:catalog",
                _ => ValueTask.FromResult(templates.GetTemplates().ToDtos().ToArray()),
                cancellationToken: ct);
            return Results.Ok(catalog);
        })
        .WithName("ListMemeTemplates")
        .WithSummary("List the curated Meme Template Library (Idea #17)")
        .Produces<IReadOnlyList<MemeTemplateDto>>(StatusCodes.Status200OK);

        group.MapGet("/{id}", (string id, IMemeTemplateService templates) =>
        {
            var tpl = templates.GetById(id);
            return tpl is null ? Results.NotFound() : Results.Ok(tpl.ToDto());
        })
        .WithName("GetMemeTemplate")
        .WithSummary("Look up a single meme template by id")
        .Produces<MemeTemplateDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        // Render endpoint — runs locally, no AI cost, no auth required.
        group.MapPost("/render", async (
            MemeTemplateRenderRequest request,
            IMemeTemplateService templates,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("MemeTemplateEndpoints");

            if (string.IsNullOrWhiteSpace(request.ImageData))
                return Results.BadRequest("ImageData is required.");
            if (string.IsNullOrWhiteSpace(request.TemplateId))
                return Results.BadRequest("TemplateId is required.");
            if (request.ZoneTexts is null || request.ZoneTexts.Count == 0)
                return Results.BadRequest("At least one zone text is required.");

            var template = templates.GetById(request.TemplateId);
            if (template is null)
                return Results.NotFound($"Template '{request.TemplateId}' not found.");

            ImageBytes imageBytes;
            try { imageBytes = ImageBytes.FromBase64(request.ImageData, request.ContentType); }
            catch (ImageValidationException ex) { return Results.BadRequest(ex.Message); }
            var imageBytesArray = imageBytes.Bytes.ToArray();

            // The interface accepts up to RequiredZoneCount; pad with empty strings
            // so a single-line template can be rendered without the client knowing details.
            var padded = request.ZoneTexts.ToList();
            while (padded.Count < template.RequiredZoneCount) padded.Add(string.Empty);

            try
            {
                var sw = Stopwatch.StartNew();
                var (data, contentType) = await templates.RenderAsync(imageBytesArray, template, padded, ct);
                sw.Stop();
                logger.LogInformation("Meme template rendered. Template={Template}, Zones={Zones}, Elapsed={Elapsed}ms",
                    template.Id, padded.Count, sw.ElapsedMilliseconds);

                return Results.Ok(new MemeTemplateRenderResponse(
                    ImageData: Convert.ToBase64String(data),
                    ContentType: contentType,
                    TemplateId: template.Id,
                    RenderedZones: padded.Count,
                    ElapsedMs: sw.ElapsedMilliseconds));
            }
            catch (ArgumentException ex)
            {
                logger.LogWarning(ex, "Meme template render validation failed");
                return Results.BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Meme template render failed");
                return Results.Problem("Failed to render meme template.", statusCode: 500, title: "Render Error");
            }
        })
        .WithName("RenderMemeTemplate")
        .WithSummary("Render a user's photo with a meme template (local, no AI cost)")
        .Produces<MemeTemplateRenderResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

        return app;
    }
}
