using PoRedoImage.Domain.Entities;

namespace PoRedoImage.Domain.Interfaces;

/// <summary>
/// Repository abstraction for user image storage (blob bytes + metadata).
/// Dependency Inversion Principle: higher layers depend on this abstraction.
/// </summary>
public interface IUserImageRepository
{
    /// <summary>Saves raw image bytes to blob storage along with optional content tags stored as blob metadata.</summary>
    Task<string> SaveBlobAsync(string userId, UserImageId imageId, byte[] bytes, string contentType, IReadOnlyList<string>? tags, CancellationToken ct = default);

    /// <summary>Saves image metadata to table storage.</summary>
    Task SaveMetadataAsync(UserImage image, CancellationToken ct = default);

    /// <summary>Returns all image metadata records for a user, ordered newest-first.</summary>
    Task<IReadOnlyList<UserImage>> GetByUserAsync(string userId, CancellationToken ct = default);

    /// <summary>Returns the raw bytes + content-type for a single image. Returns null if not found or access denied.</summary>
    Task<(byte[] Bytes, string ContentType)?> GetBlobAsync(string userId, UserImageId imageId, CancellationToken ct = default);

    /// <summary>Reads just the Tags metadata for a single image (HEAD-only, no body download).</summary>
    Task<IReadOnlyList<string>?> GetTagsAsync(string userId, UserImageId imageId, CancellationToken ct = default);

    /// <summary>Returns metadata for a single image. Returns null if not found.</summary>
    Task<UserImage?> GetMetadataAsync(string userId, UserImageId imageId, CancellationToken ct = default);

    /// <summary>Deletes a user image (blob bytes + metadata). No-op if not found.</summary>
    Task DeleteAsync(string userId, UserImageId imageId, CancellationToken ct = default);
}
