namespace PoRedoImage.Domain.Interfaces;

/// <summary>
/// Supplies grounded, machine-extracted facts about an image: text actually present in the frame,
/// region-level captions, detected objects, and a person count.
/// </summary>
/// <remarks>
/// Deliberately separate from <see cref="IVisionService"/>, which every backend implements and whose
/// shape (one description + tags) is fixed. Not every vision backend can do OCR or region captions,
/// so this is an optional capability a backend may additionally provide — callers check
/// <see cref="IsConfigured"/> and degrade rather than requiring it.
/// <para>
/// The value here is corroboration. A vision-language model interprets a scene well but will
/// confidently invent text it cannot actually read; OCR and object detection are ground truth that
/// can be handed to the model as facts rather than left to its imagination.
/// </para>
/// </remarks>
public interface ISceneDetailProvider
{
    /// <summary>True when the provider has the configuration it needs.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Extracts detail from <paramref name="imageData"/>. Returns <see cref="SceneDetails.Empty"/>
    /// rather than throwing when the backend cannot supply these features — they are an enhancement,
    /// and losing them must never fail the caller's pipeline.
    /// </summary>
    Task<SceneDetails> GetDetailsAsync(byte[] imageData, CancellationToken ct = default);
}

/// <summary>Grounded facts extracted from an image.</summary>
/// <param name="TextLines">
/// Text read from the image — signage, menu boards, garment slogans, brand names. Often the single
/// most useful detail in a scene, and the one a language model is least able to guess.
/// </param>
/// <param name="RegionCaptions">Region-level captions, each describing part of the frame.</param>
/// <param name="Objects">Distinct objects detected, most confident first.</param>
/// <param name="PeopleCount">How many people were detected.</param>
/// <param name="ElapsedMs">Wall-clock duration of the extraction.</param>
public sealed record SceneDetails(
    IReadOnlyList<string> TextLines,
    IReadOnlyList<string> RegionCaptions,
    IReadOnlyList<string> Objects,
    int PeopleCount,
    long ElapsedMs)
{
    /// <summary>Nothing was extracted — the provider is unconfigured or the features are unavailable.</summary>
    public static SceneDetails Empty { get; } = new([], [], [], 0, 0);

    /// <summary>True when there is anything worth handing to a caller.</summary>
    public bool HasAny => TextLines.Count > 0 || RegionCaptions.Count > 0 || Objects.Count > 0;

    /// <summary>
    /// Renders the details as a compact block for a model prompt. Sections are omitted entirely when
    /// empty so the model never sees "Objects: (none)" and treats the absence as meaningful.
    /// </summary>
    public string ToPromptBlock()
    {
        var parts = new List<string>();

        if (TextLines.Count > 0)
            parts.Add($"Text visible in the image (read by OCR, treat as exact): {string.Join(" | ", TextLines)}");

        if (RegionCaptions.Count > 0)
            parts.Add($"Region captions: {string.Join("; ", RegionCaptions)}");

        if (Objects.Count > 0)
            parts.Add($"Detected objects: {string.Join(", ", Objects)}");

        if (PeopleCount > 0)
            parts.Add($"People detected: {PeopleCount}");

        return string.Join("\n", parts);
    }
}
