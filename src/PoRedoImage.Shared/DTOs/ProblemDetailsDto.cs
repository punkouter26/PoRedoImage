namespace PoRedoImage.Shared.DTOs;

/// <summary>
/// Client-side view of an RFC 7807 problem response. The WASM client can't reference
/// Microsoft.AspNetCore.Mvc.ProblemDetails (server-only), so it deserializes into this
/// shape to surface <see cref="Detail"/>/<see cref="Title"/> from API error responses.
/// </summary>
public sealed class ProblemDetailsDto
{
    public string? Title { get; set; }
    public string? Detail { get; set; }
    public int? Status { get; set; }
    public string? Type { get; set; }
}
