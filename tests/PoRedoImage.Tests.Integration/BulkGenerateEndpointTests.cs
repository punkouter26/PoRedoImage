using System.Net;
using System.Net.Http.Json;
using PoRedoImage.Web.Features.BulkGenerate;

namespace PoRedoImage.Tests.Integration;

/// <summary>
/// Integration tests for /api/bulk-generate endpoints.
/// All requests are authenticated as TestAuthHandler.UserId via the test auth scheme.
/// Table Storage is not configured — storage service no-ops gracefully.
/// </summary>
public class BulkGenerateEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public BulkGenerateEndpointTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    // ─── GET /api/bulk-generate/prompts ─────────────────────────────

    [Fact]
    public async Task GetPrompts_WhenAuthenticated_NoSavedPrompts_ReturnsNotFound()
    {
        // Storage is disabled (no connection string) — LoadPromptsAsync returns null → 404
        var response = await _client.GetAsync("/api/bulk-generate/prompts");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ─── POST /api/bulk-generate/prompts ────────────────────────────

    [Fact]
    public async Task SavePrompts_ValidPayload_ReturnsNoContent()
    {
        var request = new SavePromptsRequest(
            Prompts: Enumerable.Range(1, 10).Select(i => $"Prompt {i}").ToArray()
        );

        var response = await _client.PostAsJsonAsync("/api/bulk-generate/prompts", request);

        // Service is a no-op when Storage is disabled — should still return 204
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task SavePrompts_WrongPromptCount_ReturnsBadRequest()
    {
        var request = new SavePromptsRequest(
            Prompts: ["only-one-prompt"]
        );

        var response = await _client.PostAsJsonAsync("/api/bulk-generate/prompts", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SavePrompts_PromptTooLong_ReturnsBadRequest()
    {
        var badPrompt = new string('x', 2001);
        var request = new SavePromptsRequest(
            Prompts: Enumerable.Range(0, 10).Select(i => i == 0 ? badPrompt : $"Prompt {i}").ToArray()
        );

        var response = await _client.PostAsJsonAsync("/api/bulk-generate/prompts", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SavePrompts_EmptyPrompt_ReturnsBadRequest()
    {
        var request = new SavePromptsRequest(
            Prompts: Enumerable.Range(0, 10).Select(i => i == 0 ? "" : $"Prompt {i}").ToArray()
        );

        var response = await _client.PostAsJsonAsync("/api/bulk-generate/prompts", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
