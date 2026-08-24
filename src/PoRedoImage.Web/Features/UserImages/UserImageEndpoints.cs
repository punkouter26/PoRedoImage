using System.Security.Claims;
using PoRedoImage.Application.Features.UserImages;
using PoRedoImage.Domain.Entities;
using PoRedoImage.Shared.DTOs;
using PoRedoImage.Shared.Imaging;
using PoRedoImage.Web.Features.Shared;

namespace PoRedoImage.Web.Features.UserImages;

/// <summary>
/// Minimal API endpoints for the user image gallery.
/// All endpoints require authentication — images are private to the owner.
/// GET  /api/user-images          → list gallery for current user
/// POST /api/user-images/original → save an uploaded original
/// POST /api/user-images/result   → save an AI-processed result
/// GET  /api/user-images/{id}     → stream image bytes (proxy from blob)
/// </summary>
public static class UserImageEndpoints
{
    public static void MapUserImageEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/user-images")
            .WithTags("UserImages")
            .RequireAuthorization()
            .AddEndpointFilter<PoRedoImage.Web.Features.Idempotency.IdempotencyKeyFilter>()
            .RequireAntiforgeryValidation();

        group.MapGet("/", ListImagesAsync)
            .WithName("ListUserImages")
            .WithSummary("List the current user's image gallery");

        group.MapPost("/original", SaveOriginalAsync)
            .WithName("SaveOriginalImage")
            .WithSummary("Save an uploaded original image to the gallery");

        group.MapPost("/result", SaveResultAsync)
            .WithName("SaveResultImage")
            .WithSummary("Save an AI-processed result image to the gallery");

        group.MapGet("/{id}", GetImageAsync)
            .WithName("GetUserImage")
            .WithSummary("Stream image bytes from blob storage");

        group.MapDelete("/{id}", DeleteImageAsync)
            .WithName("DeleteUserImage")
            .WithSummary("Delete a user image from the gallery");
    }

    private static async Task<IResult> ListImagesAsync(
        HttpContext context,
        IUserImageService service,
        CancellationToken ct)
    {
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId is null) return Results.Unauthorized();

        var images = await service.GetGalleryAsync(userId, ct);
        return Results.Ok(images);
    }

    private static async Task<IResult> SaveOriginalAsync(
        HttpContext context,
        SaveOriginalRequest request,
        IUserImageService service,
        CancellationToken ct)
    {
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId is null) return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.ImageData))
            return Results.BadRequest("ImageData is required.");

        ImageBytes image;
        try { image = ImageBytes.FromBase64(request.ImageData, request.ContentType); }
        catch (ImageValidationException ex) { return Results.BadRequest(ex.Message); }

        try
        {
            var result = await service.SaveOriginalAsync(userId, image.Bytes.ToArray(), image.ContentType, request.FileName, request.Tags, ct);
            return Results.Ok(result);
        }
        catch (Exception)
        {
            return Results.Problem(
                title: "Storage Service Unavailable",
                detail: "Unable to connect to Azure Storage. Please ensure Azurite or storage is running.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> SaveResultAsync(
        HttpContext context,
        SaveResultRequest request,
        IUserImageService service,
        CancellationToken ct)
    {
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId is null) return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.ImageData))
            return Results.BadRequest("ImageData is required.");

        ImageBytes image;
        try { image = ImageBytes.FromBase64(request.ImageData, request.ContentType); }
        catch (ImageValidationException ex) { return Results.BadRequest(ex.Message); }

        try
        {
            var result = await service.SaveResultAsync(userId, image.Bytes.ToArray(), image.ContentType, request.Kind, request.Tags, ct);
            return Results.Ok(result);
        }
        catch (Exception)
        {
            return Results.Problem(
                title: "Storage Service Unavailable",
                detail: "Unable to connect to Azure Storage. Please ensure Azurite or storage is running.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> GetImageAsync(
        string id,
        HttpContext context,
        IUserImageService service,
        CancellationToken ct)
    {
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId is null) return Results.Unauthorized();

        // UserImageId.TryParse is the single definition of a well-formed id — stricter than the
        // ad-hoc regex it replaces, which accepted any hex string up to 64 chars.
        if (!UserImageId.TryParse(id, out var imageId))
            return Results.BadRequest("Invalid image id.");

        var result = await service.GetImageAsync(userId, imageId, ct);
        if (result is null) return Results.NotFound();

        return Results.File(result.Value.Bytes, result.Value.ContentType);
    }

    private static async Task<IResult> DeleteImageAsync(
        string id,
        HttpContext context,
        IUserImageService service,
        CancellationToken ct)
    {
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId is null) return Results.Unauthorized();

        if (!UserImageId.TryParse(id, out var imageId))
            return Results.BadRequest("Invalid image id.");

        var deleted = await service.DeleteImageAsync(userId, imageId, ct);
        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
