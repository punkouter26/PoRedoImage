using PoImageGc.Web.Models;

namespace PoImageGc.Tests.Unit.Models;

/// <summary>
/// Unit tests for ImageAnalysisResult DTO defaults
/// </summary>
public class ImageAnalysisResultTests
{
    [Fact]
    public void Constructor_DefaultValues_AreCorrect()
    {
        var result = new ImageAnalysisResult();

        Assert.Equal(string.Empty, result.Description);
        Assert.Empty(result.Tags);
        Assert.Equal(0, result.ConfidenceScore);
        Assert.Null(result.RegeneratedImageData);
        Assert.Equal("image/png", result.RegeneratedImageContentType);
        Assert.NotNull(result.Metrics);
        Assert.Null(result.MemeImageData);
        Assert.Null(result.MemeCaption);
    }
}
