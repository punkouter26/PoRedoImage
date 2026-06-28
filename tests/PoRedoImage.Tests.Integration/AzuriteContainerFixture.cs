using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace PoRedoImage.Tests.Integration;

/// <summary>
/// Owns a SINGLE Azurite (Blob + Queue + Table) container shared by every integration test that
/// needs real storage. Previously each storage test class — and in one case each test method —
/// spun up its own <c>azurite:latest</c> container, so the container count scaled with the number
/// of classes/methods. Bound to the <see cref="AzuriteCollection"/> xUnit collection, this fixture
/// is created once for the whole collection and disposed at the end, turning N containers into 1.
///
/// Docker-absence is handled gracefully: if the container cannot start (no daemon), the fixture does
/// NOT throw — <see cref="IsAvailable"/> stays false and <see cref="ConnectionString"/> empty. Every
/// consumer is a <c>[DockerFact]</c>, which self-skips when Docker is unreachable, so the body that
/// would use the connection string never runs.
///
/// IPv4 (127.0.0.1) is used deliberately — "localhost" can resolve to IPv6 ::1, which Azurite does
/// not listen on, producing connection timeouts on Windows/CI.
/// </summary>
public sealed class AzuriteContainerFixture : IAsyncLifetime
{
    private const string WellKnownAccountKey =
        "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

    private IContainer? _container;

    /// <summary>Full Azurite connection string (Blob + Queue + Table). Empty when Docker is absent.</summary>
    public string ConnectionString { get; private set; } = "";

    /// <summary>True when the shared container started and is ready to serve requests.</summary>
    public bool IsAvailable => _container is not null && !string.IsNullOrEmpty(ConnectionString);

    async Task IAsyncLifetime.InitializeAsync()
    {
        try
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
            ConnectionString =
                "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;" +
                $"AccountKey={WellKnownAccountKey};" +
                $"BlobEndpoint=http://127.0.0.1:{blob}/devstoreaccount1;" +
                $"QueueEndpoint=http://127.0.0.1:{queue}/devstoreaccount1;" +
                $"TableEndpoint=http://127.0.0.1:{table}/devstoreaccount1;";
        }
        catch
        {
            // Docker unreachable (or image pull failed). Leave IsAvailable=false; [DockerFact]
            // consumers self-skip, so no test depends on a connection string we couldn't produce.
            if (_container is not null)
            {
                await _container.DisposeAsync();
                _container = null;
            }
            ConnectionString = "";
        }
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}

/// <summary>
/// xUnit collection that shares one <see cref="AzuriteContainerFixture"/> across all storage-backed
/// integration test classes. Classes opt in with <c>[Collection(AzuriteCollection.Name)]</c>.
/// </summary>
[CollectionDefinition(Name)]
public sealed class AzuriteCollection : ICollectionFixture<AzuriteContainerFixture>
{
    public const string Name = "Azurite";
}
