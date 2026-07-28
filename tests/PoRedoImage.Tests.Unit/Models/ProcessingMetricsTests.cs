using PoRedoImage.Shared.DTOs;

namespace PoRedoImage.Tests.Unit.Models;

public class ProcessingMetricsTests
{
    [Theory]
    [InlineData(100, 200, 300, 600)] // non-zero times sum
    [InlineData(0, 0, 0, 0)]         // all-zero times sum to zero
    public void TotalProcessingTimeMs_SumsAllTimes(
        long imageAnalysisTimeMs, long descriptionGenerationTimeMs, long imageRegenerationTimeMs, long expectedTotal)
    {
        // Arrange
        var metrics = new ProcessingMetricsDto
        {
            ImageAnalysisTimeMs = imageAnalysisTimeMs,
            DescriptionGenerationTimeMs = descriptionGenerationTimeMs,
            ImageRegenerationTimeMs = imageRegenerationTimeMs
        };

        // Act
        var total = metrics.TotalProcessingTimeMs;

        // Assert
        Assert.Equal(expectedTotal, total);
    }
}
