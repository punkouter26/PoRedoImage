using System.Net.Sockets;
using Xunit;

namespace PoRedoImage.Tests.E2EUI;

/// <summary>
/// A <see cref="FactAttribute"/> that runs only when a live PoRedoImage instance is reachable
/// at <c>E2E_BASE_URL</c> (default <c>http://localhost:5000</c>), self-skipping otherwise so the
/// Playwright UI suite stays green when the app (or installed browsers) are absent.
/// </summary>
public sealed class LiveServerFactAttribute : FactAttribute
{
    public static string BaseUrl =>
        Environment.GetEnvironmentVariable("E2E_BASE_URL") ?? "http://localhost:5000";

    private static readonly Lazy<bool> ServerReachable = new(Probe);

    public LiveServerFactAttribute()
    {
        if (!ServerReachable.Value)
        {
            Skip = $"No live instance reachable at {BaseUrl} — E2E UI test skipped.";
        }
    }

    private static bool Probe()
    {
        var uri = new Uri(BaseUrl);
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
