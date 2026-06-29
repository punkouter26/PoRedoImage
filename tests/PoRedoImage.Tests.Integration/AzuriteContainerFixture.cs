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
///
/// Container cleanup is layered (defence in depth — see ADR-019):
///   1. <c>WithCleanUp(true)</c> + Testcontainers reaper sidecar → auto-removes on container exit.
///   2. <see cref="IAsyncLifetime.DisposeAsync"/> → xUnit-driven teardown after the collection.
///   3. <see cref="AppDomain.CurrentDomain"/>.ProcessExit → graceful test-host shutdown.
///   4. <see cref="Console.CancelKeyPress"/> → Ctrl-C during long-running suites (the most common
///      accidental-leak vector in dev).
///   5. Finalizer as last resort for the rare unmanaged edge case.
/// All five routes converge on <see cref="StopAndDisposeAsync"/>, which is idempotent.
/// </summary>
public sealed class AzuriteContainerFixture : IAsyncLifetime
{
    private const string WellKnownAccountKey =
        "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

    // Stable per-process tag with a short hex suffix lets concurrent runs (parallel CI shards /
    // parallel IDE sessions) name-disambiguate their containers on the same Docker daemon, and
    // lets an out-of-band 'docker rm -f $(docker ps -aq --filter name=poredoimage-test-azurite-)'
    // prune target them without touching unrelated containers. The 48-char cap leaves headroom
    // under Docker's 64-char name limit.
    private static readonly string ContainerName =
        $"poredoimage-test-azurite-{Guid.NewGuid():N}"[..48];

    private IContainer? _container;
    private bool _shutdownHandlersRegistered;
    private bool _disposed;

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
                .WithName(ContainerName)
                .WithPortBinding(0, 10000) // blob  → random host port
                .WithPortBinding(0, 10001) // queue → random host port
                .WithPortBinding(0, 10002) // table → random host port
                .WithCommand("azurite", "--blobHost", "0.0.0.0", "--queueHost", "0.0.0.0",
                    "--tableHost", "0.0.0.0", "--loose")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(10002))
                .WithCleanUp(true) // Belt: Docker auto-remove on container exit + reaper sidecar.
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

            RegisterShutdownHandlersOnce();
        }
        catch
        {
            // Docker unreachable (or image pull failed). Leave IsAvailable=false; [DockerFact]
            // consumers self-skip, so no test depends on a connection string we couldn't produce.
            // Even on the failure path we still try to dispose the partial container — Build()
            // can return a partially-constructed IContainer that owns Docker-side state.
            await StopAndDisposeAsync();
            ConnectionString = "";
        }
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await StopAndDisposeAsync();
    }

    /// <summary>
    /// Idempotent teardown. Safe to call from xUnit's <c>DisposeAsync</c>, the ProcessExit handler,
    /// the CancelKeyPress handler, the catch-all in InitializeAsync, and the finalizer — multiple
    /// invocations are no-ops beyond the first. Swallows secondary errors so a teardown-time
    /// exception can never mask a primary test failure.
    /// </summary>
    private async Task StopAndDisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        var container = _container;
        _container = null;
        if (container is null) return;

        try
        {
            // StopAsync is best-effort; even if it throws (e.g. daemon vanished mid-suite),
            // DisposeAsync below still removes the container from the daemon side.
            try { await container.StopAsync(); } catch { /* best-effort */ }
        }
        finally
        {
            try { await container.DisposeAsync(); }
            catch { /* swallow to keep teardown exception-isolated */ }
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Process- and Ctrl-C-level teardown hooks. Registered lazily on the first successful start so
    /// we never leak delegates when Docker was unreachable (the catch above already disposed).
    /// </summary>
    private void RegisterShutdownHandlersOnce()
    {
        if (_shutdownHandlersRegistered) return;
        _shutdownHandlersRegistered = true;

        // Graceful process shutdown — the runtime gives managed code one final chance to run.
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            StopAndDisposeAsync().GetAwaiter().GetResult();

        // Console-attached Ctrl-C / SIGINT — the most common accidental-leak vector for
        // long-running test suites. Letting xUnit continue (Cancel = false) keeps collection
        // teardown ordered; we only front-run the container stop so the daemon-side state
        // vanishes immediately rather than waiting for xUnit's collection dispose path.
        Console.CancelKeyPress += (_, args) =>
        {
            StopAndDisposeAsync().GetAwaiter().GetResult();
            args.Cancel = false;
        };
    }

    /// <summary>
    /// Finalizer as the very last safety net (e.g. test process aborted with SIGKILL and Testcontainers
    /// reaper didn't reach the daemon in time). GC.SuppressFinalize is invoked by the successful
    /// teardown paths so this only fires for genuinely leaked fixtures.
    /// </summary>
    ~AzuriteContainerFixture() => StopAndDisposeAsync().GetAwaiter().GetResult();
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
