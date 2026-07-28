using PoRedoImage.Domain.Entities;

namespace PoRedoImage.Shared.DTOs;

/// <summary>DTO returned from the /api/user-images list endpoint.</summary>
public record UserImageDto(
    string Id,
    string FileName,
    string ContentType,
    UserImageKind Kind,
    DateTimeOffset CreatedAt,
    long SizeBytes,
    string ImageUrl,
    IReadOnlyList<string> Tags);

public record SaveOriginalRequest(string ImageData, string ContentType, string FileName, IReadOnlyList<string>? Tags = null);
public record SaveResultRequest(string ImageData, string ContentType, UserImageKind Kind, IReadOnlyList<string>? Tags = null);
public record SaveImageResponse(string Id, string ImageUrl);
