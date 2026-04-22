using PoRedoImage.Shared.DTOs;

namespace PoRedoImage.Tests.Unit.Models;

public class ProcessingMetricsTests
{
    [Fact]
    public void TotalProcessingTimeMs_SumsAllTimes()
    {
        // Arrange
        var metrics = new ProcessingMetricsDto
        {
            ImageAnalysisTimeMs = 100,
            DescriptionGenerationTimeMs = 200,
            ImageRegenerationTimeMs = 300
        };

        // Act
        var total = metrics.TotalProcessingTimeMs;

        // Assert
        Assert.Equal(600, total);
    }

    [Fact]
    public void TotalProcessingTimeMs_ZeroWhenAllZero()
    {
        // Arrange
        var metrics = new ProcessingMetricsDto();

        // Act & Assert
        Assert.Equal(0, metrics.TotalProcessingTimeMs);
    }
}
