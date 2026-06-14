using Azure.Data.Tables;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace PoRedoImage.Tests.Integration;

/// <summary>
/// Real integration tests for BulkPromptStorageService against a Docker-hosted Azurite container.
/// Validates that prompt persistence (save → load → verify) works against actual Table Storage.
/// </summary>
[Trait("Category", "Docker")]
public sealed class AzuriteTableStorageIntegrationTests : IAsyncLifetime
{
    private IContainer? _container;
    private string? _connectionString;

    public async Task InitializeAsync()
    {
        _container = new ContainerBuilder()
            .WithImage("mcr.microsoft.com/azure-storage/azurite:latest")
            .WithPortBinding(0, 10002) // random host port → avoids conflicts
            .WithCommand("azurite-table", "--tableHost", "0.0.0.0", "--loose")
            .WithCleanUp(true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(10002))
            .Build();

        await _container.StartAsync();
        var port = _container.GetMappedPublicPort(10002);
        _connectionString =
            $"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;" +
            $"AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;" +
            $"TableEndpoint=http://127.0.0.1:{port}/devstoreaccount1;";
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

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
