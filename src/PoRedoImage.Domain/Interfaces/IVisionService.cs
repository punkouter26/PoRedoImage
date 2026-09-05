namespace PoRedoImage.Domain.Interfaces;

/// <summary>
/// Domain service interface for image vision analysis.
/// Interface Segregation Principle (SOLID-I): clients only depend on what they use.
/// </summary>
public interface IVisionService
{
    Task<(string Description, IReadOnlyList<string> Tags, double ConfidenceScore, long ElapsedMs, string? FallbackReason)>
        AnalyzeAsync(byte[] imageData, CancellationToken ct = default);
}
