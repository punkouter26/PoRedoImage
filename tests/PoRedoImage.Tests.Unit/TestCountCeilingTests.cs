using System.Reflection;
using PoRedoImage.Tests.Shared;
using Xunit;

namespace PoRedoImage.Tests.Unit;

/// <summary>
/// Structural guardrail for the "100/50/25 Rule": the Unit tier is capped at 100 test methods to
/// keep execution instantaneous and prevent cross-tier redundancy creeping in. Counts methods (a
/// [Theory] counts once regardless of InlineData cases) and excludes this meta-test itself.
/// If this fails, either delete redundant unit tests or promote behaviour to the integration tier.
///
/// The ceiling is a BUDGET, not a target — being under it is healthy headroom, not unused capacity.
/// DTO/validation CONTRACT tests deliberately live in the Integration tier (see
/// tests/PoRedoImage.Tests.Integration/Contracts/), which freed Unit headroom and keeps this tier to
/// pure no-I/O logic. Do not backfill to "use up" the budget.
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
