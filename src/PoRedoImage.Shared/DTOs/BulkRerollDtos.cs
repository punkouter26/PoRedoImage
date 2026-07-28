namespace PoRedoImage.Shared.DTOs;

/// <summary>
/// Request to generate N parallel re-rolled variations from a winning prompt.
/// Idea #11 — "One-Tap Re-roll x3": spawns N variations seeded from the winner's prompt,
/// letting users pivot from "almost perfect" results without writing a new prompt.
/// </summary>
public record BulkRerollRequest(
    string ImageData,
    string ContentType,
    string SeedPrompt,
    int Count = 3,
    string? ImageGenModelId = null);

/// <summary>
/// A single re-rolled variation result.
/// </summary>
public record BulkRerollVariation(
    int Index,
    string ImageData,
    string ContentType);

/// <summary>
/// Response containing all re-rolled variations in order.
/// </summary>
public record BulkRerollResponse(
    IReadOnlyList<BulkRerollVariation> Variations,
    int Requested,
    int Succeeded,
    long ElapsedMs);
