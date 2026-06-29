using PoRedoImage.Shared.DTOs;

namespace PoRedoImage.Tests.Integration.Contracts;

/// <summary>
/// Contract tests for the <see cref="ImageAnalysisRequest"/> DTO defaults and mapping.
/// Lives in the Integration tier (not Unit) alongside the validation contract it pairs with —
/// see the "Contractual Integration Testing" audit item.
/// </summary>
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
