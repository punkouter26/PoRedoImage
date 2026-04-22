namespace PoRedoImage.Domain.Interfaces;

public interface IGenerativeAiService
{
    Task<(string EnhancedDescription, int TokensUsed, long ElapsedMs)>
        EnhanceDescriptionAsync(string description, IReadOnlyList<string> tags, int targetLength, CancellationToken ct = default);

    Task<(byte[] ImageData, string ContentType, long ElapsedMs)>
        GenerateImageAsync(string description, CancellationToken ct = default);

    Task<(string TopText, string BottomText, int TokensUsed, long ElapsedMs)>
        GenerateMemeCaptionAsync(IReadOnlyList<string> tags, CancellationToken ct = default);

    Task<string> DescribePersonAsync(byte[] imageData, CancellationToken ct = default);
}
