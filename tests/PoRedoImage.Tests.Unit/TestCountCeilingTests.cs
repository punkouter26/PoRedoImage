using System.Reflection;
using Xunit;

namespace PoRedoImage.Tests.Unit;

/// <summary>
/// Structural guardrail for the "100/50/25/25 Rule": the Unit tier is capped at 100 test methods to
/// keep execution instantaneous and prevent cross-tier redundancy creeping in. Counts methods (a
/// [Theory] counts once regardless of InlineData cases) and excludes this meta-test itself.
/// If this fails, either delete redundant unit tests or promote behaviour to the integration tier.
/// </summary>
public sealed class TestCountCeilingTests
{
    private const int UnitCeiling = 100;

    [Fact]
    public void Unit_tier_stays_within_its_method_ceiling()
    {
        var count = TestCounting.CountFactMethods(
            Assembly.GetExecutingAssembly(), excluding: typeof(TestCountCeilingTests));

        Assert.True(count <= UnitCeiling,
            $"Unit tier has {count} test methods, exceeding the ceiling of {UnitCeiling}. " +
            "Remove redundancy or move I/O-bound tests to the integration tier.");
    }
}

/// <summary>Shared reflection helper for counting xUnit fact/theory methods in a tier.</summary>
internal static class TestCounting
{
    public static int CountFactMethods(Assembly assembly, Type excluding)
        => assembly.GetTypes()
            .Where(t => t != excluding)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            // TheoryAttribute, DockerFact, LiveServerFact all derive from FactAttribute.
            .Count(m => m.GetCustomAttributes(typeof(FactAttribute), inherit: true).Length > 0);
}
