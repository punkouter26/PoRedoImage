using System.Net;
using System.Net.Http.Json;
using PoRedoImage.Shared.DTOs;

namespace PoRedoImage.Tests.Integration;

/// <summary>
/// When the browser has already run vision locally, the server must not run it again — otherwise a
/// user who chose a free on-device model still pays for a metered Azure call.
/// </summary>
public class PrecomputedVisionTests(MockedServicesWebApplicationFactory factory)
    : IClassFixture<MockedServicesWebApplicationFactory>
{
    [Fact]
    public async Task Analyze_WithPrecomputedDescription_SkipsVisionAndReportsZeroAnalysisTime()
    {
        var client = factory.CreateClient();

        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var request = new ImageAnalysisRequest
        {
            ImageData = Convert.ToBase64String(imageBytes),
            ContentType = "image/png",
            FileName = "test.png",
            Mode = ProcessingMode.MemeGeneration,
            PrecomputedDescription = "a lighthouse at dusk",
            PrecomputedTags = ["lighthouse", "dusk"],
        };

        var response = await client.PostAsJsonWithTokenAsync("/api/images/analyze", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ImageAnalysisResponse>();
        Assert.NotNull(body);
        Assert.Equal(0, body.Metrics!.ImageAnalysisTimeMs);
        Assert.Contains("lighthouse", body.Tags);
    }

    [Fact]
    public async Task Analyze_WithPrecomputedPrompt_SkipsEnhancementAndUsesTheClientsPrompt()
    {
        var client = factory.CreateClient();

        const string onDevicePrompt = "lighthouse at dusk, long exposure, cobalt sky, salt spray";
        var request = new ImageAnalysisRequest
        {
            ImageData = Convert.ToBase64String(new byte[] { 0x89, 0x50, 0x4E, 0x47 }),
            ContentType = "image/png",
            FileName = "test.png",
            Mode = ProcessingMode.ImageRegeneration,
            PrecomputedDescription = "a lighthouse at dusk",
            PrecomputedTags = ["lighthouse", "dusk"],
            PrecomputedEnhancedPrompt = onDevicePrompt,
        };

        var response = await client.PostAsJsonWithTokenAsync("/api/images/analyze", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ImageAnalysisResponse>();
        Assert.NotNull(body);

        // The client's prompt must survive verbatim, and no tokens may be billed for rewriting it.
        // A server that "improved" it would have spent money the user explicitly opted out of.
        Assert.Equal(onDevicePrompt, body.Description);
        Assert.Equal(0, body.Metrics!.DescriptionGenerationTimeMs);
        Assert.Equal(0, body.Metrics.DescriptionTokensUsed);
    }
}
