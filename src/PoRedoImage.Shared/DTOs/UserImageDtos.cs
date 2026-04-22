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
    string ImageUrl);

public record SaveOriginalRequest(string ImageData, string ContentType, string FileName);
public record SaveResultRequest(string ImageData, string ContentType, UserImageKind Kind);
public record SaveImageResponse(string Id, string ImageUrl);
