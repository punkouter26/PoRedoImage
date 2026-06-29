using System.Reflection;
using PoRedoImage.Tests.Shared;
using Xunit;

namespace PoRedoImage.Tests.Integration;

/// <summary>
/// Structural guardrail for the "100/50/25 Rule": the Integration tier is capped at 50 test
/// methods. Counts fact/theory methods (excluding this meta-test). Keeps the tier focused on
/// endpoint/storage contracts rather than re-testing unit logic.
/// </summary>
public sealed class TestCountCeilingTests
{
    private const int IntegrationCeiling = 50;

    [Fact]
    public void Integration_tier_stays_within_its_method_ceiling()
    {
        var count = TestCounting.CountFactMethods(
            Assembly.GetExecutingAssembly(), excluding: typeof(TestCountCeilingTests));

        Assert.True(count <= IntegrationCeiling,
            $"Integration tier has {count} test methods, exceeding the ceiling of {IntegrationCeiling}.");
    }
}
