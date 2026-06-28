using Azure.Data.Tables;

namespace PoRedoImage.Tests.Integration;

/// <summary>
/// Verifies that the Docker-hosted Azurite container starts and accepts connections. Uses the shared
/// <see cref="AzuriteContainerFixture"/> — the container started once for the whole collection IS the
/// proof of the Docker/Azurite usage requirement, so this no longer spins up a throwaway container
/// per run.
/// </summary>
[Collection(AzuriteCollection.Name)]
public class TestcontainersAzuriteHealthCheckTests
{
    private readonly AzuriteContainerFixture _azurite;

    public TestcontainersAzuriteHealthCheckTests(AzuriteContainerFixture azurite) => _azurite = azurite;

    [DockerFact]
    public async Task SharedAzuriteContainer_IsRunningAndAcceptsConnections()
    {
        Assert.True(_azurite.IsAvailable,
            "Shared Azurite container should be running when Docker is available.");

        // Prove the Table endpoint actually answers — a successful CreateIfNotExists round-trips to
        // the listener, which is what the original per-test container start was verifying.
        var serviceClient = new TableServiceClient(_azurite.ConnectionString);
        var table = serviceClient.GetTableClient("HealthProbe");
        await table.CreateIfNotExistsAsync();

        Assert.NotNull(serviceClient.Uri);
    }
}
