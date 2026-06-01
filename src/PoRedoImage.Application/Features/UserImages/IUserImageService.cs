using PoRedoImage.Domain.Entities;
using PoRedoImage.Shared.DTOs;

namespace PoRedoImage.Application.Features.UserImages;

/// <summary>
/// Application service for the user image gallery.
/// Single Responsibility: gallery operations only — no AI pipeline logic.
/// </summary>
public interface IUserImageService
{
    Task<SaveImageResponse> SaveOriginalAsync(string userId, byte[] bytes, string contentType, string fileName, CancellationToken ct = default);
    Task<SaveImageResponse> SaveResultAsync(string userId, byte[] bytes, string contentType, UserImageKind kind, CancellationToken ct = default);
    Task<IReadOnlyList<UserImageDto>> GetGalleryAsync(string userId, CancellationToken ct = default);
    Task<(byte[] Bytes, string ContentType)?> GetImageAsync(string userId, string imageId, CancellationToken ct = default);

    /// <summary>Deletes a user image from blob and metadata storage. Returns false if not found.</summary>
    Task<bool> DeleteImageAsync(string userId, string imageId, CancellationToken ct = default);
}
