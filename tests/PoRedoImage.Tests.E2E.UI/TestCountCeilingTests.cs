using System.Reflection;
using PoRedoImage.Tests.Shared;
using Xunit;

namespace PoRedoImage.Tests.E2E.UI;

/// <summary>
/// Structural guardrail for the "25 Rule" on the E2E UI suite (C# Playwright).
/// Ceiling = 25 fact methods in this assembly, excluding this meta-test. The
/// companion PoRedoImage.Tests.E2E.ApiSmoke project enforces the same ceiling
/// independently on its own assembly.
/// </summary>
public sealed class TestCountCeilingTests
{
    private const int UiCeiling = 25;

    [Fact]
    public void E2E_ui_suite_stays_within_ceiling()
    {
        var count = TestCounting.CountFactMethods(
            Assembly.GetExecutingAssembly(), excluding: typeof(TestCountCeilingTests));
        Assert.True(count <= UiCeiling,
            $"E2E UI suite has {count} test methods, exceeding the ceiling of {UiCeiling}.");
    }
}
