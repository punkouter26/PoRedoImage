using PoImageGc.Web.Features.Diagnostics;

namespace PoImageGc.Tests.Unit.Features;

/// <summary>
/// Unit tests for DiagnosticsEndpoints MaskValue logic
/// </summary>
public class DiagnosticsEndpointsTests
{
    [Fact]
    public void MaskValue_NullValue_ReturnsNotSet()
    {
        // Act
        var result = DiagnosticsEndpoints.MaskValue(null);

        // Assert
        Assert.Equal("(not set)", result);
    }

    [Fact]
    public void MaskValue_EmptyString_ReturnsNotSet()
    {
        // Act
        var result = DiagnosticsEndpoints.MaskValue("");

        // Assert
        Assert.Equal("(not set)", result);
    }

    [Fact]
    public void MaskValue_ShortValue_FullyMasked()
    {
        // Act
        var result = DiagnosticsEndpoints.MaskValue("abc");

        // Assert
        Assert.Equal("***", result);
    }

    [Fact]
    public void MaskValue_LongValue_ShowsStartAndEnd()
    {
        // Arrange
        var value = "sk-abcdefghij123456";

        // Act
        var result = DiagnosticsEndpoints.MaskValue(value);

        // Assert
        Assert.NotNull(result);
        Assert.StartsWith("sk-a", result);
        Assert.EndsWith("3456", result);
        Assert.Contains("*", result);
        Assert.Equal(value.Length, result.Length);
    }

    [Fact]
    public void MaskValue_ExactlyEightChars_FullyMasked()
    {
        // Act
        var result = DiagnosticsEndpoints.MaskValue("12345678");

        // Assert
        Assert.Equal("********", result);
    }

    [Fact]
    public void MaskValue_NineChars_PartiallyMasked()
    {
        // Arrange — 9 chars, visibleStart = 2, visibleEnd = 2
        var value = "123456789";

        // Act
        var result = DiagnosticsEndpoints.MaskValue(value);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(9, result.Length);
        Assert.Contains("*", result);
    }
}
