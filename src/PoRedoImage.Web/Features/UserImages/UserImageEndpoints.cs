using System.Security.Claims;
using PoRedoImage.Application.Services;
using PoRedoImage.Domain.Entities;
using PoRedoImage.Shared.DTOs;

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
            .RequireAuthorization();

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

        byte[] bytes;
        try { bytes = Convert.FromBase64String(request.ImageData); }
        catch { return Results.BadRequest("ImageData must be valid base64."); }

        var result = await service.SaveOriginalAsync(userId, bytes, request.ContentType, request.FileName, ct);
        return Results.Ok(result);
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

        byte[] bytes;
        try { bytes = Convert.FromBase64String(request.ImageData); }
        catch { return Results.BadRequest("ImageData must be valid base64."); }

        var result = await service.SaveResultAsync(userId, bytes, request.ContentType, request.Kind, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetImageAsync(
        string id,
        HttpContext context,
        IUserImageService service,
        CancellationToken ct)
    {
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId is null) return Results.Unauthorized();

        // Sanitize id — only allow hex GUIDs (32 hex chars, no separators)
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64 || !System.Text.RegularExpressions.Regex.IsMatch(id, @"^[a-fA-F0-9]+$"))
            return Results.BadRequest("Invalid image id.");

        var result = await service.GetImageAsync(userId, id, ct);
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

        if (string.IsNullOrWhiteSpace(id) || id.Length > 64 || !System.Text.RegularExpressions.Regex.IsMatch(id, @"^[a-fA-F0-9]+$"))
            return Results.BadRequest("Invalid image id.");

        var deleted = await service.DeleteImageAsync(userId, id, ct);
        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
