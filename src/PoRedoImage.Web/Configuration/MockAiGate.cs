using PoRedoImage.Application.Configuration;
using PoRedoImage.Shared.Configuration;

namespace PoRedoImage.Web.Configuration;

/// <summary>
/// Single decision point for whether <c>Mocks:UseMockAi=true</c> is HONORED.
///
/// Policy: mock AI is ONLY available in the <see cref="PoEnvironments.Test"/> environment
/// (integration tests + E2E). The local Development loop and Production must use real
/// services end-to-end — Development reads real keys from Key Vault (<c>az login</c> +
/// "Key Vault Secrets User" RBAC on kv-poshared); Production reads the same vault via the
/// App Service <c>@Microsoft.KeyVault(...)</c> references in <c>infra/main.bicep</c>.
///
/// The reasoning, distilled:
///
/// 1. Mocks are a TEST FIXTURE, not a dev shortcut. They hide the real failure modes
///    that only surface against live Azure (rate limits, 401s from rotated keys,
///    deployment-name mismatches, region-unavailable Computer Vision features). The dev
///    loop's purpose is to surface those failures fast; mocks defeat that.
/// 2. Devs reading this code should not have to wonder whether <c>Mocks:UseMockAi=true</c>
///    in an old env var, leftover from a debugging session, is silently degrading their
///    local loop to canned output. The gate makes the answer "no, regardless" in Dev.
/// 3. Tests that genuinely need mocks set the env var and run under <c>ASPNETCORE_ENVIRONMENT=Test</c>
///    — same place as <c>FakeAuthHandler</c> and the rest of the test-only infrastructure.
///    Centralising the gate here keeps that contract in one file.
///
/// When the flag is set but the env is NOT Test, this logs a one-line warning so a
/// developer who DID set it knows it's being ignored — instead of failing silently or
/// (worse) silently degrading to mock output.
/// </summary>
public static class MockAiGate
{
    /// <summary>
    /// True when <c>Mocks:UseMockAi=true</c> is honored. False when the flag is unset OR
    /// when the host is not in the <c>Test</c> environment.
    /// </summary>
    public static bool IsEnabled(IConfiguration configuration, IWebHostEnvironment env)
    {
        var requested = ConfigValue.Bool(configuration, ConfigKeys.MocksUseMockAi);
        if (!requested) return false;

        if (env.IsEnvironment(PoEnvironments.Test)) return true;

        // The flag is set but the env forbids it. Log once so the developer is told the
        // setting exists and is being ignored, rather than discovering later that "the
        // banner is gone but my image upload isn't doing anything".
        LogIgnoredInNonTest(env);
        return false;
    }

    private static bool _warned;

    private static void LogIgnoredInNonTest(IWebHostEnvironment env)
    {
        // The bootstrap logger is the only thing in scope from the very first config read;
        // once Serilog is wired, the same warning lands in the file/App Insights pipeline.
        // Guard with a static flag so we don't spam the log on every per-request read.
        if (_warned) return;
        _warned = true;

        Serilog.Log.Warning(
            "Mocks:UseMockAi=true was set but the host is running in '{Env}' (not Test). " +
            "Mock mode is gated to the Test environment only — Development and Production must " +
            "use real services. The flag is being IGNORED; remove Mocks__UseMockAi or move to " +
            "ASPNETCORE_ENVIRONMENT=Test if you genuinely need mocks.",
            env.EnvironmentName);
    }
}
