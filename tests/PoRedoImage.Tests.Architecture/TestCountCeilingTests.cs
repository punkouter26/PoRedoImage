using System.Reflection;
using PoRedoImage.Tests.Shared;
using Xunit;

namespace PoRedoImage.Tests.Architecture;

/// <summary>
/// Structural guardrail matching the other tiers. This suite is capped at 10 test METHODS, which is
/// deliberately tiny: architectural coverage grows by adding entries to
/// <see cref="ArchitectureRules.All"/>, not by adding test methods. A [Theory] counts once however
/// many rules it drives, so the registry can hold any number of rules under this ceiling.
///
/// If this ever fails, the fix is almost certainly to fold the new check into the rule registry
/// rather than to raise the number.
/// </summary>
public sealed class TestCountCeilingTests
{
    private const int ArchitectureCeiling = 10;

    [Fact]
    public void Architecture_tier_stays_within_its_method_ceiling()
    {
        var count = TestCounting.CountFactMethods(
            Assembly.GetExecutingAssembly(), excluding: typeof(TestCountCeilingTests));

        Assert.True(count <= ArchitectureCeiling,
            $"Architecture tier has {count} test methods, exceeding the ceiling of {ArchitectureCeiling}. " +
            "Add architectural coverage as entries in ArchitectureRules.All, not as new test methods.");
    }
}
