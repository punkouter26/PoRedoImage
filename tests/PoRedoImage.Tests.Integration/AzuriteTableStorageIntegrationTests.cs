using Azure.Data.Tables;

namespace PoRedoImage.Tests.Integration;

/// <summary>
/// Real integration tests for BulkPromptStorageService against a Docker-hosted Azurite container.
/// Validates that prompt persistence (save → load → verify) works against actual Table Storage.
/// Uses the shared <see cref="AzuriteContainerFixture"/> (one container for the whole collection)
/// rather than starting its own.
/// </summary>
[Trait("Category", "Docker")]
[Collection(AzuriteCollection.Name)]
public sealed class AzuriteTableStorageIntegrationTests
{
    private readonly string _connectionString;

    public AzuriteTableStorageIntegrationTests(AzuriteContainerFixture azurite)
        => _connectionString = azurite.ConnectionString;

    [DockerFact]
    public async Task SaveAndLoadPrompts_RoundTrip_Succeeds()
    {
        var serviceClient = new TableServiceClient(_connectionString);
        var tableClient = serviceClient.GetTableClient("BulkPrompts");
        await tableClient.CreateIfNotExistsAsync();

        var userId = "integration-test-user";
        var prompts = new[] { "A cyberpunk city at night", "Watercolor forest in autumn" };

        // Upsert
        var entity = new Azure.Data.Tables.TableEntity("prompts", userId)
        {
            ["PromptsJson"] = System.Text.Json.JsonSerializer.Serialize(prompts)
        };
        await tableClient.UpsertEntityAsync(entity);

        // Read back
        var loaded = await tableClient.GetEntityAsync<Azure.Data.Tables.TableEntity>("prompts", userId);
        var json = loaded.Value.GetString("PromptsJson");
        var result = System.Text.Json.JsonSerializer.Deserialize<string[]>(json!);

        Assert.NotNull(result);
        Assert.Equal(prompts.Length, result!.Length);
        Assert.Equal(prompts[0], result[0]);
        Assert.Equal(prompts[1], result[1]);
    }
}
