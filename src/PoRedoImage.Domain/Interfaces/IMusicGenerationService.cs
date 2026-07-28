namespace PoRedoImage.Domain.Interfaces;

/// <summary>
/// Vendor-agnostic abstraction for generating a music track that performs supplied lyrics.
/// </summary>
/// <remarks>
/// Deliberately separate from <see cref="IImageGenerationService"/> and
/// <see cref="IGenerativeAiService"/> so the music provider can be swapped or mocked on its own.
/// The distinction that matters when choosing an implementation: this contract performs the
/// <em>given</em> lyrics. Instrumental text-to-audio models (Stable Audio, MusicGen) take a prompt
/// that <em>describes</em> the audio and cannot satisfy it.
/// </remarks>
public interface IMusicGenerationService
{
    /// <summary>
    /// Generates a track performing <paramref name="lyrics"/> in the requested style.
    /// </summary>
    /// <param name="lyrics">
    /// Lyrics to perform, structured with section tags (<c>[Verse]</c>, <c>[Chorus]</c>).
    /// </param>
    /// <param name="stylePrompt">Musical direction — genre, tempo, mood.</param>
    /// <returns>
    /// The encoded audio and its content type, or <c>Refused</c> when the provider's safety
    /// filter declined the prompt. A refusal is an expected outcome, not an exception: callers
    /// are meant to soften and retry, so it is modelled in the result rather than thrown.
    /// </returns>
    Task<MusicGenerationResult> GenerateAsync(
        string lyrics, string stylePrompt, CancellationToken ct = default);

    /// <summary>True when the provider has the configuration it needs to be called.</summary>
    bool IsConfigured { get; }
}

/// <summary>Outcome of a single music-generation attempt.</summary>
/// <param name="Audio">Encoded audio bytes; empty when <paramref name="Refused"/> is true.</param>
/// <param name="ContentType">MIME type of <paramref name="Audio"/> (e.g. <c>audio/mpeg</c>).</param>
/// <param name="ElapsedMs">Wall-clock duration of the provider call.</param>
/// <param name="Refused">True when the provider's safety filter declined the prompt.</param>
/// <param name="RefusalReason">Provider-supplied explanation, when it gave one.</param>
public sealed record MusicGenerationResult(
    byte[] Audio,
    string ContentType,
    long ElapsedMs,
    bool Refused = false,
    string? RefusalReason = null)
{
    public static MusicGenerationResult FromRefusal(long elapsedMs, string? reason) =>
        new([], string.Empty, elapsedMs, Refused: true, RefusalReason: reason);
}
