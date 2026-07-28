using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Entities;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Shared.DTOs;

namespace PoRedoImage.Application.Features.UserImages;

public sealed class UserImageService(
    IUserImageRepository repository,
    ILogger<UserImageService> logger) : IUserImageService
{
    public async Task<SaveImageResponse> SaveOriginalAsync(string userId, byte[] bytes, string contentType, string fileName, CancellationToken ct = default)
    {
        var image = UserImage.Create(userId, fileName, contentType, UserImageKind.Original, bytes.Length);
        await repository.SaveBlobAsync(userId, image.Id, bytes, contentType, ct);
        await repository.SaveMetadataAsync(image, ct);
        logger.LogInformation("Saved original image {Id} for user {UserId}", image.Id, userId);
        return new SaveImageResponse(image.Id.Value, $"/api/user-images/{image.Id}");
    }

    public async Task<SaveImageResponse> SaveResultAsync(string userId, byte[] bytes, string contentType, UserImageKind kind, CancellationToken ct = default)
    {
        var fileName = kind switch
        {
            UserImageKind.Regeneration => "regenerated.png",
            UserImageKind.Meme => "meme.png",
            UserImageKind.BulkVariation => "variation.png",
            _ => "result.png"
        };
        var image = UserImage.Create(userId, fileName, contentType, kind, bytes.Length);
        await repository.SaveBlobAsync(userId, image.Id, bytes, contentType, ct);
        await repository.SaveMetadataAsync(image, ct);
        logger.LogInformation("Saved {Kind} result image {Id} for user {UserId}", kind, image.Id, userId);
        return new SaveImageResponse(image.Id.Value, $"/api/user-images/{image.Id}");
    }

    public async Task<IReadOnlyList<UserImageDto>> GetGalleryAsync(string userId, CancellationToken ct = default)
    {
        var images = await repository.GetByUserAsync(userId, ct);
        return images
            .Select(i => new UserImageDto(i.Id.Value, i.FileName, i.ContentType, i.Kind, i.CreatedAt, i.SizeBytes, $"/api/user-images/{i.Id}"))
            .ToList()
            .AsReadOnly();
    }

    public Task<(byte[] Bytes, string ContentType)?> GetImageAsync(string userId, UserImageId imageId, CancellationToken ct = default) =>
        repository.GetBlobAsync(userId, imageId, ct);

    public async Task<bool> DeleteImageAsync(string userId, UserImageId imageId, CancellationToken ct = default)
    {
        var metadata = await repository.GetMetadataAsync(userId, imageId, ct);
        if (metadata is null) return false;

        await repository.DeleteAsync(userId, imageId, ct);
        logger.LogInformation("Deleted image {Id} for user {UserId}", imageId, userId);
        return true;
    }
}
