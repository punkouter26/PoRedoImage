using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace PoRedoImage.Tests.Integration;

/// <summary>
/// A <see cref="CustomWebApplicationFactory"/> that owns a dynamically-provisioned Azurite
/// container for its entire lifetime (Testcontainers for .NET). The container is started in
/// <see cref="IAsyncLifetime.InitializeAsync"/> — before the host is built — and its IPv4
/// connection string is injected via <see cref="CustomWebApplicationFactory.StorageConnectionString"/>,
/// so storage-backed endpoints run against real Table/Blob storage with a guaranteed-clean slate.
/// Disposed with the fixture, eliminating stale state between test runs.
///
/// IPv4 (127.0.0.1) is used deliberately — "localhost" can resolve to IPv6 ::1, which Azurite
/// does not listen on, producing connection timeouts on Windows/CI.
/// </summary>
public sealed class AzuriteWebApplicationFactory : CustomWebApplicationFactory, IAsyncLifetime
{
    private const string WellKnownAccountKey =
        "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

    private IContainer? _container;
    private string _connectionString = "";

    protected override string StorageConnectionString => _connectionString;

    async Task IAsyncLifetime.InitializeAsync()
    {
        _container = new ContainerBuilder()
            .WithImage("mcr.microsoft.com/azure-storage/azurite:latest")
            .WithPortBinding(0, 10000) // blob  → random host port
            .WithPortBinding(0, 10001) // queue → random host port
            .WithPortBinding(0, 10002) // table → random host port
            .WithCommand("azurite", "--blobHost", "0.0.0.0", "--queueHost", "0.0.0.0",
                "--tableHost", "0.0.0.0", "--loose")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(10002))
            .WithCleanUp(true)
            .Build();

        await _container.StartAsync();

        var blob = _container.GetMappedPublicPort(10000);
        var queue = _container.GetMappedPublicPort(10001);
        var table = _container.GetMappedPublicPort(10002);
        _connectionString =
            "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;" +
            $"AccountKey={WellKnownAccountKey};" +
            $"BlobEndpoint=http://127.0.0.1:{blob}/devstoreaccount1;" +
            $"QueueEndpoint=http://127.0.0.1:{queue}/devstoreaccount1;" +
            $"TableEndpoint=http://127.0.0.1:{table}/devstoreaccount1;";
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
        Dispose();
    }
}
