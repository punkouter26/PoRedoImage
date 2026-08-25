using PoRedoImage.Shared.DTOs;

namespace PoRedoImage.Application.Features.BulkGenerate;

/// <summary>
/// Fan-out image generation for the bulk board: one source image, many prompts.
/// </summary>
/// <remarks>
/// This orchestration used to live in the endpoint. It sits here so the transport owns only
/// framing — the endpoint decides that results go out as NDJSON, this decides how many calls
/// run at once, what a failed slot yields, and how a re-roll seed is derived.
/// </remarks>
public interface IBulkGenerationService
{
    /// <summary>Whether the provider behind <paramref name="imageGenModelId"/> is usable.</summary>
    bool IsConfigured(string? imageGenModelId);

    /// <summary>
    /// Generates one image per prompt, yielding each the moment it lands rather than at the end.
    /// Slots complete out of order; <see cref="BulkBatchItem.Index"/> maps each back to its prompt.
    /// A slot that fails yields an item carrying <see cref="BulkBatchItem.Error"/> instead of
    /// aborting its siblings.
    /// </summary>
    IAsyncEnumerable<BulkBatchItem> GenerateBatchAsync(
        IReadOnlyList<string> prompts,
        byte[] source,
        string? imageGenModelId,
        CancellationToken ct = default);

    /// <summary>
    /// Generates <paramref name="count"/> variations of one winning prompt, each with a distinct
    /// seed. Failed slots are dropped, so the result may hold fewer than requested.
    /// </summary>
    Task<BulkRerollResponse> RerollAsync(
        string seedPrompt,
        byte[] source,
        int count,
        string? imageGenModelId,
        CancellationToken ct = default);
}
