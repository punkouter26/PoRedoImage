using System.Text.Json;

namespace PoRedoImage.Tests.Architecture;

/// <summary>
/// Drives every rule in <see cref="ArchitectureRules"/> and publishes the outcome for the scorecard.
/// </summary>
/// <remarks>
/// Two methods, on purpose. The [Theory] is the BUILD GATE — a boundary violation fails the run and
/// names the offending types. The [Fact] is the SCORECARD FEED — it always passes and simply
/// records pass/fail per rule to JSON, so a failing architecture never also breaks the reporting
/// that is supposed to measure it. Two methods against this tier's ceiling, however many rules.
/// </remarks>
public sealed class ArchitectureRuleTests
{
    public static TheoryData<string> RuleIds()
    {
        var data = new TheoryData<string>();
        foreach (var rule in ArchitectureRules.All) data.Add(rule.Id);
        return data;
    }

    [Theory]
    [MemberData(nameof(RuleIds))]
    public void Architectural_boundary_holds(string ruleId)
    {
        var rule = ArchitectureRules.All.Single(r => r.Id == ruleId);
        var violations = rule.Evaluate();

        Assert.True(violations.Count == 0,
            $"[{rule.Category}] {rule.Description}{Environment.NewLine}" +
            $"Violating types ({violations.Count}):{Environment.NewLine}  " +
            string.Join($"{Environment.NewLine}  ", violations));
    }

    /// <summary>
    /// Writes <c>artifacts/architecture-results.json</c> for <c>SCRIPTS/generate-scorecard.ps1</c>.
    /// Always passes: this reports the score, it does not enforce it (the [Theory] above does).
    /// </summary>
    [Fact]
    public void Publish_architecture_results_for_scorecard()
    {
        var evaluated = ArchitectureRules.All
            .Select(rule =>
            {
                // A rule that throws (e.g. an assembly failed to load) is recorded as a failure
                // with the reason, rather than taking down the reporting run.
                try
                {
                    var violations = rule.Evaluate();
                    return new
                    {
                        id = rule.Id,
                        category = rule.Category,
                        description = rule.Description,
                        passed = violations.Count == 0,
                        violations
                    };
                }
                catch (Exception ex)
                {
                    return new
                    {
                        id = rule.Id,
                        category = rule.Category,
                        description = rule.Description,
                        passed = false,
                        violations = (IReadOnlyList<string>)[$"rule threw: {ex.GetType().Name}: {ex.Message}"]
                    };
                }
            })
            .ToList();

        var passed = evaluated.Count(r => r.passed);
        var total = evaluated.Count;

        var payload = new
        {
            tool = "NetArchTest",
            totalRules = total,
            passedRules = passed,
            failedRules = total - passed,
            // The scorecard's NetArchTest component, already normalised 0-100.
            passRate = total == 0 ? 0d : Math.Round(passed * 100d / total, 2),
            rules = evaluated
        };

        var outputDir = Path.Combine(RepositoryRoot(), "artifacts");
        Directory.CreateDirectory(outputDir);
        File.WriteAllText(
            Path.Combine(outputDir, "architecture-results.json"),
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));

        Assert.Equal(total, evaluated.Count);
    }

    /// <summary>
    /// Walks up from the test binaries to the directory holding the solution file. Anchoring on a
    /// real repo marker keeps the artifact path stable whether the run starts from the IDE, the
    /// CLI, or a different working directory.
    /// </summary>
    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PoRedoImage.slnx")))
            dir = dir.Parent;

        return dir?.FullName
            ?? throw new InvalidOperationException(
                "Could not locate PoRedoImage.slnx walking up from " + AppContext.BaseDirectory);
    }
}
