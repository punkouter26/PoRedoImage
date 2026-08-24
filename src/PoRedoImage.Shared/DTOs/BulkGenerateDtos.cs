namespace PoRedoImage.Shared.DTOs;

public class BulkGenerateImageResult
{
    public int Index { get; set; }
    public BulkGenerateStatus Status { get; set; } = BulkGenerateStatus.Pending;
    public string? ImageUrl { get; set; }
    public string? Prompt { get; set; }
    public string? ErrorMessage { get; set; }
}

public enum BulkGenerateStatus
{
    Pending,
    Processing,
    Complete,
    Failed
}

public record BulkDescribeRequest(string ImageData, string ContentType);
public record BulkDescribeResponse(string Description);

public record BulkVariationRequest(string ImageData, string ContentType, string Prompt, string? ImageGenModelId = null);
public record BulkVariationResponse(string ImageData, string ContentType);

/// <summary>Save the caller's 10 bulk-generation prompts. Shared so the WASM client and BFF agree on shape.</summary>
public record SavePromptsRequest(string[] Prompts);

/// <summary>
/// Generate every variation in one request, streaming each slot back as it lands.
/// </summary>
/// <remarks>
/// <para>
/// This replaces N separate <c>/variation</c> posts driven by a <c>for</c> loop in the browser. That
/// shape had two costs. The obvious one is latency: ten strictly sequential round-trips at ~4s each
/// is ~40s of wall clock, against a stated target of 45s p95 that it was only just meeting. The
/// quieter one is bandwidth — the SOURCE IMAGE was re-uploaded with every single request, so a 4MB
/// photo became ~53MB of base64 upload for one batch.
/// </para>
/// <para>
/// Here the image travels once and the server fans out under its own concurrency cap, streaming
/// NDJSON so slots still appear one by one exactly as they did before. Nothing about the user's
/// experience of watching the board fill in changes; it just stops taking three times as long.
/// </para>
/// </remarks>
/// <param name="Prompts">One prompt per slot, already <c>&lt;PERSON&gt;</c>-substituted by the client.</param>
public record BulkBatchRequest(
    string ImageData,
    string ContentType,
    string[] Prompts,
    string? ImageGenModelId = null);

/// <summary>One slot's outcome, emitted as a single NDJSON line the moment that slot finishes.</summary>
/// <param name="Index">Which prompt this answers — slots complete out of order under concurrency.</param>
/// <param name="ImageData">Base64 image, or null when <paramref name="Error"/> is set.</param>
/// <param name="Error">Why this slot failed. One failure never fails the batch.</param>
public record BulkBatchItem(
    int Index,
    string? ImageData,
    string? ContentType,
    string? Error);
