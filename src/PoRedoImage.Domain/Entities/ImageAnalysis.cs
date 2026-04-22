namespace PoRedoImage.Domain.Entities;

/// <summary>
/// Domain entity representing the result of an image analysis operation.
/// Follows the Entity pattern (DDD) — encapsulates identity + state.
/// </summary>
public sealed class ImageAnalysis
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Description { get; private set; } = string.Empty;
    public IReadOnlyList<string> Tags { get; private set; } = [];
    public double ConfidenceScore { get; private set; }
    public DateTimeOffset AnalyzedAt { get; private set; } = DateTimeOffset.UtcNow;

    // Private ctor — use factory method (Creational: Factory Method pattern)
    private ImageAnalysis() { }

    public static ImageAnalysis Create(string description, IEnumerable<string> tags, double confidenceScore)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(tags);

        return new ImageAnalysis
        {
            Description = description,
            Tags = tags.ToList().AsReadOnly(),
            ConfidenceScore = confidenceScore
        };
    }
}
