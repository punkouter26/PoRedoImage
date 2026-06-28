using System.Net;
using System.Net.Http.Json;
using PoRedoImage.Shared.DTOs;

namespace PoRedoImage.Tests.Integration;

/// <summary>
/// True end-to-end storage integration: drives the /api/bulk-generate/prompts endpoints against a
/// live Azurite container managed inside the <see cref="AzuriteWebApplicationFactory"/> lifecycle.
/// Unlike <see cref="BulkGenerateEndpointTests"/> (storage disabled → no-op assertions), this proves
/// save → load actually persists through the repository and Table API mapping with a clean slate.
/// </summary>
[Collection(AzuriteCollection.Name)]
public sealed class BulkPromptStorageRoundTripTests : IDisposable
{
    private readonly AzuriteWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public BulkPromptStorageRoundTripTests(AzuriteContainerFixture azurite)
    {
        // Reuse the single shared Azurite container's connection string rather than spinning up a
        // dedicated container for this class. [DockerFact] below self-skips when Docker is absent.
        _factory = new AzuriteWebApplicationFactory(azurite.ConnectionString);
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [DockerFact]
    public async Task SavePrompts_thenGetPrompts_roundTripsThroughAzurite()
    {
        var prompts = Enumerable.Range(1, 10).Select(i => $"Prompt {i}").ToArray();

        var save = await _client.PostAsJsonAsync("/api/bulk-generate/prompts",
            new SavePromptsRequest(prompts));
        Assert.Equal(HttpStatusCode.NoContent, save.StatusCode);

        var get = await _client.GetAsync("/api/bulk-generate/prompts");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);

        var loaded = await get.Content.ReadFromJsonAsync<string[]>();
        Assert.NotNull(loaded);
        Assert.Equal(prompts, loaded);
    }
}
