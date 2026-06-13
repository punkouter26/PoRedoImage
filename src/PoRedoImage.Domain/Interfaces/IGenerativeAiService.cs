namespace PoRedoImage.Domain.Interfaces;

public interface IGenerativeAiService
{
    Task<(string EnhancedDescription, int TokensUsed, long ElapsedMs)>
        EnhanceDescriptionAsync(string description, IReadOnlyList<string> tags, int targetLength, CancellationToken ct = default);

    Task<(string TopText, string BottomText, int TokensUsed, long ElapsedMs)>
        GenerateMemeCaptionAsync(IReadOnlyList<string> tags, CancellationToken ct = default);

    Task<string> DescribePersonAsync(byte[] imageData, CancellationToken ct = default);

    /// <summary>
    /// Generates a single short-form caption using a caller-supplied persona system prompt.
    /// Used by the Meme Caption Battle (Idea #5) to produce 8 candidates with different voices.
    /// </summary>
    Task<(string Caption, int TokensUsed, long ElapsedMs)>
        GenerateCaptionAsync(IReadOnlyList<string> tags, string systemPrompt, CancellationToken ct = default);
}
