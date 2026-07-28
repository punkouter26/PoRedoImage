namespace PoRedoImage.Domain.Entities;

/// <summary>
/// Domain entity representing an image stored in the user's personal gallery.
/// Covers both uploaded originals and AI-processed results (regen, meme, bulk variation).
/// </summary>
public sealed class UserImage
{
    public string UserId { get; init; } = string.Empty;
    public UserImageId Id { get; init; } = UserImageId.New();
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = "image/jpeg";
    public UserImageKind Kind { get; init; } = UserImageKind.Original;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public long SizeBytes { get; init; }

    public static UserImage Create(string userId, string fileName, string contentType, UserImageKind kind, long sizeBytes) =>
        new()
        {
            Id = UserImageId.New(),
            UserId = userId,
            FileName = fileName,
            ContentType = contentType,
            Kind = kind,
            SizeBytes = sizeBytes
        };
}
