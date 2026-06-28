using System.Reflection;
using Xunit;

namespace PoRedoImage.Tests.E2E;

/// <summary>
/// Structural guardrail for the "100/50/25/25 Rule": the E2E tier is split into E2EUI (Playwright)
/// and E2EAPI (HTTP smoke), each capped at 25 test methods. Classification is by convention — test
/// classes whose name contains "Ui" are the UI suite; all others are the API suite. Excludes this
/// meta-test. Keeps the slowest, most brittle tiers small and high-signal.
/// </summary>
public sealed class TestCountCeilingTests
{
    private const int UiCeiling = 25;
    private const int ApiCeiling = 25;

    [Fact]
    public void E2E_ui_and_api_suites_stay_within_their_ceilings()
    {
        var factMethods = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => t != typeof(TestCountCeilingTests))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes(typeof(FactAttribute), inherit: true).Length > 0)
                .Select(m => m.DeclaringType!.Name))
            .ToList();

        var ui = factMethods.Count(n => n.Contains("Ui", StringComparison.OrdinalIgnoreCase));
        var api = factMethods.Count - ui;

        Assert.True(ui <= UiCeiling, $"E2EUI has {ui} test methods, exceeding the ceiling of {UiCeiling}.");
        Assert.True(api <= ApiCeiling, $"E2EAPI has {api} test methods, exceeding the ceiling of {ApiCeiling}.");
    }
}
