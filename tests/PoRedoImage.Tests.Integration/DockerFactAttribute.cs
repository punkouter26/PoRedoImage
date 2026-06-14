using System.Diagnostics;
using Xunit;

namespace PoRedoImage.Tests.Integration;

/// <summary>
/// A <see cref="FactAttribute"/> that executes only when a Docker daemon is reachable,
/// and self-skips otherwise. Replaces hard-coded <c>[Fact(Skip = "...")]</c> on
/// Testcontainers tests so they actually run on dev machines and CI agents that have
/// Docker — with zero manual cleanup — while staying green where Docker is absent.
/// </summary>
/// <remarks>
/// The daemon probe (<c>docker info</c>) runs once per test session and is cached, so the
/// per-test cost is a single dictionary lookup. Validates Docker connectivity before the
/// Testcontainers runtime attempts to allocate a container (NET_TEST Task 1).
/// </remarks>
public sealed class DockerFactAttribute : FactAttribute
{
    private static readonly Lazy<bool> DockerAvailable = new(ProbeDocker);

    public DockerFactAttribute()
    {
        if (!DockerAvailable.Value)
        {
            Skip = "Docker daemon not reachable — Testcontainers test skipped.";
        }
    }

    private static bool ProbeDocker()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("docker", "info")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process is null)
            {
                return false;
            }

            return process.WaitForExit(15_000) && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
