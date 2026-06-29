using System.Reflection;
using Xunit;

namespace PoRedoImage.Tests.Shared;

/// <summary>
/// Shared reflection helper backing the per-tier "100/50/25 Rule" guardrails
/// (<c>TestCountCeilingTests</c> in each test project). Linked into every test project via
/// <c>&lt;Compile Include="..\TestCounting.cs" /&gt;</c> so the counting logic lives in exactly one
/// place instead of being copy-pasted per tier.
/// </summary>
internal static class TestCounting
{
    private const BindingFlags MethodFlags =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    // TheoryAttribute, DockerFact, and LiveServerFact all derive from FactAttribute, so a single
    // FactAttribute check (a [Theory] counts once regardless of InlineData cases) covers every tier.
    private static bool IsFactMethod(MethodInfo m) =>
        m.GetCustomAttributes(typeof(FactAttribute), inherit: true).Length > 0;

    /// <summary>Counts fact/theory methods in <paramref name="assembly"/>, excluding the meta-test type.</summary>
    public static int CountFactMethods(Assembly assembly, Type excluding)
        => assembly.GetTypes()
            .Where(t => t != excluding)
            .SelectMany(t => t.GetMethods(MethodFlags))
            .Count(IsFactMethod);

    /// <summary>
    /// Like <see cref="CountFactMethods"/> but returns the declaring-type name for each fact method,
    /// so tiers that sub-classify by convention (e.g. E2E UI vs API) can partition the counts.
    /// </summary>
    public static IReadOnlyList<string> FactMethodDeclaringTypeNames(Assembly assembly, Type excluding)
        => assembly.GetTypes()
            .Where(t => t != excluding)
            .SelectMany(t => t.GetMethods(MethodFlags)
                .Where(IsFactMethod)
                .Select(m => m.DeclaringType!.Name))
            .ToList();
}
