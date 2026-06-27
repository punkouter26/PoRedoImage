using System.Net.Sockets;
using Xunit;

namespace PoRedoImage.Tests.E2EAPI;

/// <summary>
/// A <see cref="FactAttribute"/> that runs only when a live PoRedoImage instance is
/// reachable at <c>E2E_BASE_URL</c> (default <c>http://localhost:5000</c>), and self-skips
/// otherwise. Mirrors <c>DockerFactAttribute</c> so the E2E API suite stays green on
/// machines where the app isn't running, yet executes for real against a started instance.
/// </summary>
public sealed class LiveServerFactAttribute : FactAttribute
{
    private static readonly Lazy<bool> ServerReachable = new(Probe);

    public LiveServerFactAttribute()
    {
        if (!ServerReachable.Value)
        {
            Skip = $"No live instance reachable at {E2EApiFixture.ResolveBaseUrl()} — E2E API test skipped.";
        }
    }

    private static bool Probe()
    {
        var uri = new Uri(E2EApiFixture.ResolveBaseUrl());
        try
        {
            using var client = new TcpClient();
            return client.ConnectAsync(uri.Host, uri.Port).Wait(2000) && client.Connected;
        }
        catch
        {
            return false;
        }
    }
}
