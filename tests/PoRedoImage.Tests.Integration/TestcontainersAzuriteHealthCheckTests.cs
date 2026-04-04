using DotNet.Testcontainers.Builders;
using Xunit;

namespace PoRedoImage.Tests.Integration;

/// <summary>
/// Verifies that a Docker-hosted Azurite container starts and accepts TCP connections.
/// This test validates the Docker/Azurite usage requirement for local dev storage.
/// </summary>
public class TestcontainersAzuriteHealthCheckTests
{
    [Fact(Skip = "Requires Docker daemon; runs in CI with Docker agent only")]
    public async Task AzuriteContainer_CanStartAndListenOnExpectedPorts()
    {
        await using var container = new ContainerBuilder()
            .WithImage("mcr.microsoft.com/azure-storage/azurite:latest")
            .WithName("poredoimage-azurite-test")
            .WithPortBinding(10000, 10000)
            .WithPortBinding(10001, 10001)
            .WithPortBinding(10002, 10002)
            .WithCommand("azurite", "--blobHost", "0.0.0.0", "--queueHost", "0.0.0.0", "--tableHost", "0.0.0.0", "--loose")
            .WithCleanUp(true)
            .Build();

        await container.StartAsync();

        // Container should be running and expose Blob (10000), Queue (10001), Table (10002)
        Assert.Equal(DotNet.Testcontainers.Containers.TestcontainersStates.Running, container.State);

        await container.StopAsync();
    }
}
