using PoRedoImage.Web.Features.Diagnostics;

namespace PoRedoImage.Tests.Unit.Features;

/// <summary>
/// Unit tests for DiagnosticsEndpoints MaskValue logic
/// </summary>
public class DiagnosticsEndpointsTests
{
    [Theory]
    [InlineData(null, "(not set)")]                          // null -> not set
    [InlineData("", "(not set)")]                             // empty -> not set
    [InlineData("abc", "***")]                                // short value -> fully masked
    [InlineData("sk-abcdefghij123456", "sk-a***********3456")] // long value -> shows start and end
    [InlineData("12345678", "********")]                       // exactly 8 chars -> fully masked
    [InlineData("123456789", "12*****89")]                     // 9 chars -> partially masked (visibleStart=2, visibleEnd=2)
    public void MaskValue_ReturnsExpectedMaskedForm(string? input, string expected)
    {
        // Act
        var result = DiagnosticsEndpoints.MaskValue(input);

        // Assert
        Assert.Equal(expected, result);
    }
}
