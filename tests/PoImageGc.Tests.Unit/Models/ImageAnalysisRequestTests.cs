using PoImageGc.Shared.Models;

namespace PoImageGc.Tests.Unit.Models;

public class ImageAnalysisRequestTests
{
    [Fact]
    public void Constructor_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var request = new ImageAnalysisRequest();

        // Assert
        Assert.Equal(string.Empty, request.ImageData);
        Assert.Equal(string.Empty, request.ContentType);
        Assert.Equal(string.Empty, request.FileName);
        Assert.Equal(200, request.DescriptionLength);
        Assert.Equal(ProcessingMode.ImageRegeneration, request.Mode);
    }

    [Fact]
    public void DescriptionLength_ValidRange_Accepted()
    {
        // Arrange
        var request = new ImageAnalysisRequest();

        // Act
        request.DescriptionLength = 300;

        // Assert
        Assert.Equal(300, request.DescriptionLength);
    }
}
