using System.Reflection;
using PoRedoImage.Tests.Shared;
using Xunit;

namespace PoRedoImage.Tests.E2E;

/// <summary>
/// Structural guardrail for the "100/50/25 Rule": the E2E tier is split into UI (Playwright) and
/// API (HTTP smoke), each capped at 25 test methods. Classification is by convention — test classes
/// whose name contains "Ui" are the UI suite; all others are the API suite. Excludes this meta-test.
/// Keeps the slowest, most brittle tiers small and high-signal.
/// </summary>
public sealed class TestCountCeilingTests
{
    private const int UiCeiling = 25;
    private const int ApiCeiling = 25;

    [Fact]
    public void E2E_ui_and_api_suites_stay_within_their_ceilings()
    {
        var declaringTypeNames = TestCounting.FactMethodDeclaringTypeNames(
            Assembly.GetExecutingAssembly(), excluding: typeof(TestCountCeilingTests));

        var ui = declaringTypeNames.Count(n => n.Contains("Ui", StringComparison.OrdinalIgnoreCase));
        var api = declaringTypeNames.Count - ui;

        Assert.True(ui <= UiCeiling, $"E2E UI suite has {ui} test methods, exceeding the ceiling of {UiCeiling}.");
        Assert.True(api <= ApiCeiling, $"E2E API suite has {api} test methods, exceeding the ceiling of {ApiCeiling}.");
    }
}
