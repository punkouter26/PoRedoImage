namespace PoRedoImage.Domain.Interfaces;

/// <summary>
/// An optional capability for backends that can answer <see cref="IVisionService"/> and
/// <see cref="ISceneDetailProvider"/> from a <em>single</em> upstream request.
/// </summary>
/// <remarks>
/// <para>
/// This exists because Rap Roast was making two Azure Computer Vision calls with the same bytes:
/// <c>AnalyzeAsync(Caption|Tags)</c> from the orchestrator, then <c>GetDetailsAsync(Read|Objects|
/// People|DenseCaptions)</c> from the scene describer. Azure CV accepts all of those visual features
/// in one request, so the second call was a round-trip and a charge bought for nothing.
/// </para>
/// <para>
/// It is a separate interface rather than a method on either of the others because it is genuinely
/// optional: Ollama and the browser-local backends have no notion of dense captions or OCR, and a
/// caller must be able to ask "can you do this in one?" and fall back to two parallel calls when the
/// answer is no. <see cref="SupportsCombinedAnalysis"/> is that question.
/// </para>
/// </remarks>
public interface ICombinedVisionAnalyzer
{
    /// <summary>
    /// True when <see cref="AnalyzeAllAsync"/> will actually save a round-trip. False means the
    /// caller should run the two services separately (in parallel).
    /// </summary>
    bool SupportsCombinedAnalysis { get; }

    /// <summary>
    /// Runs description, tags and grounded scene detail in one upstream request.
    /// </summary>
    Task<CombinedVisionResult> AnalyzeAllAsync(byte[] imageData, CancellationToken ct = default);
}

/// <summary>Everything one combined vision request yields.</summary>
/// <param name="Description">Caption text, or a synthesised description when the region has no Caption feature.</param>
/// <param name="Tags">Tags above the configured confidence floor.</param>
/// <param name="ConfidenceScore">Caption confidence, or 0 when synthesised.</param>
/// <param name="Details">Grounded facts — OCR, objects, region captions, people count.</param>
/// <param name="ElapsedMs">Wall-clock duration of the single call.</param>
public sealed record CombinedVisionResult(
    string Description,
    IReadOnlyList<string> Tags,
    double ConfidenceScore,
    SceneDetails Details,
    long ElapsedMs);
