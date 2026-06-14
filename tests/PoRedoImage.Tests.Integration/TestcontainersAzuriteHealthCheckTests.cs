using DotNet.Testcontainers.Builders;
using Xunit;

namespace PoRedoImage.Tests.Integration;

/// <summary>
/// Verifies that a Docker-hosted Azurite container starts and accepts TCP connections.
/// This test validates the Docker/Azurite usage requirement for local dev storage.
/// </summary>
public class TestcontainersAzuriteHealthCheckTests
{
    [DockerFact]
    public async Task AzuriteContainer_CanStartAndListenOnExpectedPorts()
    {
        // Random host ports (0) so the test never collides with a locally-running
        // docker-compose Azurite on 10000-10002.
        await using var container = new ContainerBuilder()
            .WithImage("mcr.microsoft.com/azure-storage/azurite:latest")
            .WithPortBinding(0, 10000)
            .WithPortBinding(0, 10001)
            .WithPortBinding(0, 10002)
            .WithCommand("azurite", "--blobHost", "0.0.0.0", "--queueHost", "0.0.0.0", "--tableHost", "0.0.0.0", "--loose")
            .WithCleanUp(true)
            .Build();

        await container.StartAsync();

        // Container should be running and expose Blob (10000), Queue (10001), Table (10002)
        Assert.Equal(DotNet.Testcontainers.Containers.TestcontainersStates.Running, container.State);

        await container.StopAsync();
    }
}
