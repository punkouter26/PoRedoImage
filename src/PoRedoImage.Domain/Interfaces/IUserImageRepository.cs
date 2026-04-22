using PoRedoImage.Domain.Entities;

namespace PoRedoImage.Domain.Interfaces;

/// <summary>
/// Repository abstraction for user image storage (blob bytes + metadata).
/// Dependency Inversion Principle: higher layers depend on this abstraction.
/// </summary>
public interface IUserImageRepository
{
    /// <summary>Saves raw image bytes to blob storage. Returns the blob URL (or opaque id path).</summary>
    Task<string> SaveBlobAsync(string userId, string imageId, byte[] bytes, string contentType, CancellationToken ct = default);

    /// <summary>Saves image metadata to table storage.</summary>
    Task SaveMetadataAsync(UserImage image, CancellationToken ct = default);

    /// <summary>Returns all image metadata records for a user, ordered newest-first.</summary>
    Task<IReadOnlyList<UserImage>> GetByUserAsync(string userId, CancellationToken ct = default);

    /// <summary>Returns the raw bytes + content-type for a single image. Returns null if not found or access denied.</summary>
    Task<(byte[] Bytes, string ContentType)?> GetBlobAsync(string userId, string imageId, CancellationToken ct = default);

    /// <summary>Returns metadata for a single image. Returns null if not found.</summary>
    Task<UserImage?> GetMetadataAsync(string userId, string imageId, CancellationToken ct = default);

    /// <summary>Deletes a user image (blob bytes + metadata). No-op if not found.</summary>
    Task DeleteAsync(string userId, string imageId, CancellationToken ct = default);
}
