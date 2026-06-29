using System.Reflection;
using PoRedoImage.Tests.Shared;
using Xunit;

namespace PoRedoImage.Tests.E2E.ApiSmoke;

/// <summary>
/// Structural guardrail for the "25 Rule" on the E2E API suite (pure HTTP smoke).
/// Ceiling = 25 fact methods in this assembly, excluding this meta-test. Keeps the
/// slowest tier small and high-signal. The companion PoRedoImage.Tests.E2E.UI
/// project enforces the same ceiling independently on its own assembly.
/// </summary>
public sealed class TestCountCeilingTests
{
    private const int ApiCeiling = 25;

    [Fact]
    public void E2E_api_suite_stays_within_ceiling()
    {
        var count = TestCounting.CountFactMethods(
            Assembly.GetExecutingAssembly(), excluding: typeof(TestCountCeilingTests));
        Assert.True(count <= ApiCeiling,
            $"E2E API suite has {count} test methods, exceeding the ceiling of {ApiCeiling}.");
    }
}
