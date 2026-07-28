using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Entities;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Shared.DTOs;

namespace PoRedoImage.Application.Features.UserImages;

public sealed class UserImageService(
    IUserImageRepository repository,
    ILogger<UserImageService> logger) : IUserImageService
{
    public async Task<SaveImageResponse> SaveOriginalAsync(string userId, byte[] bytes, string contentType, string fileName, IReadOnlyList<string>? tags = null, CancellationToken ct = default)
    {
        var image = UserImage.Create(userId, fileName, contentType, UserImageKind.Original, bytes.Length, tags);
        await repository.SaveBlobAsync(userId, image.Id, bytes, contentType, image.Tags, ct);
        await repository.SaveMetadataAsync(image, ct);
        logger.LogInformation("Saved original image {Id} for user {UserId}", image.Id, userId);
        return new SaveImageResponse(image.Id.Value, $"/api/user-images/{image.Id}");
    }

    public async Task<SaveImageResponse> SaveResultAsync(string userId, byte[] bytes, string contentType, UserImageKind kind, IReadOnlyList<string>? tags = null, CancellationToken ct = default)
    {
        var fileName = kind switch
        {
            UserImageKind.Regeneration => "regenerated.png",
            UserImageKind.Meme => "meme.png",
            UserImageKind.BulkVariation => "variation.png",
            _ => "result.png"
        };
        var image = UserImage.Create(userId, fileName, contentType, kind, bytes.Length, tags);
        await repository.SaveBlobAsync(userId, image.Id, bytes, contentType, image.Tags, ct);
        await repository.SaveMetadataAsync(image, ct);
        logger.LogInformation("Saved {Kind} result image {Id} for user {UserId}", kind, image.Id, userId);
        return new SaveImageResponse(image.Id.Value, $"/api/user-images/{image.Id}");
    }

    public async Task<IReadOnlyList<UserImageDto>> GetGalleryAsync(string userId, CancellationToken ct = default)
    {
        var images = await repository.GetByUserAsync(userId, ct);
        var list = new List<UserImageDto>(images.Count);
        foreach (var i in images)
        {
            // Tags live on the blob, not in the table — pull them per-row so the gallery can
            // filter by content later. The metadata call is a HEAD-equivalent (no body download),
            // and we degrade gracefully on storage errors so a single broken blob can't blank the
            // whole gallery.
            var tags = await repository.GetTagsAsync(userId, i.Id, ct) ?? [];
            list.Add(new UserImageDto(
                i.Id.Value,
                i.FileName,
                i.ContentType,
                i.Kind,
                i.CreatedAt,
                i.SizeBytes,
                $"/api/user-images/{i.Id}",
                tags));
        }
        return list.AsReadOnly();
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
