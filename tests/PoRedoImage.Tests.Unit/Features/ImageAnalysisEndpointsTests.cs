using PoRedoImage.Web.Features.ImageAnalysis;

namespace PoRedoImage.Tests.Unit.Features;

/// <summary>
/// Unit tests for the content-filter classification used by the /api/images/analyze error mapping.
/// </summary>
/// <remarks>
/// The regression these pin: Azure OpenAI reports a filtered prompt as <c>content_filter</c>, while
/// OpenAI.com uses <c>content_policy_violation</c>. The endpoint originally matched only the latter,
/// so every Azure rejection fell through to the catch-all and surfaced as an opaque HTTP 500
/// ("An error occurred while processing your image") instead of the intended actionable 422.
/// A single [Theory] keeps the tier under its 100-method ceiling.
/// </remarks>
public class ImageAnalysisEndpointsTests
{
    [Theory]
    // Azure OpenAI — the exact shape observed in logs/poredoimage-20260728.log.
    [InlineData(
        "HTTP 400 (content_filter)\nParameter: prompt\n\nThe response was filtered due to the prompt "
        + "triggering Azure OpenAI's content management policy.",
        true)]
    // Azure OpenAI, response-side filtering rather than prompt-side.
    [InlineData("HTTP 400 (content_filter)\nParameter: response", true)]
    // OpenAI.com — the code the original guard clause matched. Must keep working.
    [InlineData("The request was rejected: content_policy_violation", true)]
    // Casing must not matter: the code is echoed from an upstream payload we do not control.
    [InlineData("HTTP 400 (CONTENT_FILTER)", true)]
    // Unrelated failures must NOT be reclassified as content filtering — they have their own
    // handlers (401/403 -> 503, empty response -> 500) and must keep reaching them.
    [InlineData("HTTP 401 (invalid_api_key)", false)]
    [InlineData("HTTP 429 (rate_limit_exceeded)", false)]
    [InlineData("HTTP 400 (context_length_exceeded)", false)]
    [InlineData("", false)]
    public void IsContentFiltered_ClassifiesUpstreamRefusals(string message, bool expected)
    {
        Assert.Equal(expected, ImageAnalysisEndpoints.IsContentFiltered(message));
    }
}
