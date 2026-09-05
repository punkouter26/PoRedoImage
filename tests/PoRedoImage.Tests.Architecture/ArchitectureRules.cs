using System.Reflection;
using NetArchTest.Rules;

namespace PoRedoImage.Tests.Architecture;

/// <summary>One architectural boundary rule, evaluated by reflection over the built assemblies.</summary>
/// <param name="Id">Stable identifier; used as the scorecard key, so do not rename casually.</param>
/// <param name="Category">Grouping surfaced in the scorecard breakdown.</param>
/// <param name="Description">Human-readable statement of the invariant, phrased as the rule.</param>
/// <param name="Evaluate">Runs the rule and returns the offending type names (empty = pass).</param>
public sealed record ArchitectureRule(
    string Id,
    string Category,
    string Description,
    Func<IReadOnlyList<string>> Evaluate);

/// <summary>
/// The architectural rule registry.
/// </summary>
/// <remarks>
/// <para>
/// Rules are DATA, not one test method each, and that is deliberate rather than stylistic. The
/// repo enforces per-tier test-method ceilings (<c>TestCountCeilingTests</c>); at the time this was
/// added the Unit tier stood at 96/100 and Integration at 47/50, so a rule-per-[Fact] suite could
/// not fit in either tier without breaking the build gate. A registry driven by one [Theory] costs
/// a single method against this tier's ceiling however many rules it holds, so rules can be added
/// freely — which is the point of an architecture suite.
/// </para>
/// <para>
/// It also gives the scorecard a real denominator: Architecture Pass Rate = passed / total, over
/// exactly the list below.
/// </para>
/// </remarks>
public static class ArchitectureRules
{
    private const string Domain = "PoRedoImage.Domain";
    private const string Application = "PoRedoImage.Application";
    private const string Infrastructure = "PoRedoImage.Infrastructure";
    private const string Shared = "PoRedoImage.Shared";
    private const string Client = "PoRedoImage.Client";
    private const string Web = "PoRedoImage.Web";

    private static Assembly AssemblyOf(string name) => Assembly.Load(name);

    /// <summary>
    /// Renders a rule result's offending types. The eNhancedEdition exposes an
    /// <c>Explanation</c> per failing type saying WHICH forbidden dependency it took — worth
    /// surfacing, because "Domain must not depend on Infrastructure" plus a bare type name still
    /// leaves you grepping for the offending reference.
    /// </summary>
    private static IReadOnlyList<string> Violations(TestResult result)
    {
        if (result.IsSuccessful) return [];

        var failing = result.FailingTypes;
        if (failing is null || failing.Count == 0) return ["<rule failed but reported no types>"];

        return [.. failing.Select(t => string.IsNullOrWhiteSpace(t.Explanation)
            ? t.FullName
            : $"{t.FullName} — {t.Explanation}")];
    }

    /// <summary>Types in <paramref name="ns"/> must not reference any of <paramref name="forbidden"/>.</summary>
    private static IReadOnlyList<string> Forbid(string assembly, string ns, params string[] forbidden)
    {
        var result = Types.InAssembly(AssemblyOf(assembly))
            .That().ResideInNamespace(ns)
            .ShouldNot().HaveDependencyOnAny(forbidden)
            .GetResult();

        return Violations(result);
    }

    /// <summary>
    /// Vertical-slice encapsulation: a feature slice may not reach into a sibling slice.
    /// </summary>
    /// <remarks>
    /// <c>Features.Shared</c> is exempt by design — it is the deliberate cross-slice vocabulary
    /// (<c>AuthorizationPolicies</c>, <c>AntiforgeryExtensions</c>), and the codebase comments call
    /// that out explicitly ("consuming slices depend on the shared vocabulary rather than on this
    /// slice"). Exempting it is what makes the rule mean "no slice-to-slice coupling" rather than
    /// "no coupling at all".
    /// </remarks>
    private static IReadOnlyList<string> SliceIsolation(string slice, IReadOnlyList<string> allSlices)
    {
        var siblings = allSlices
            .Where(s => !string.Equals(s, slice, StringComparison.Ordinal))
            .Where(s => !CrossCutting.Contains(s, StringComparer.Ordinal))
            .Select(s => $"{Web}.Features.{s}")
            .ToArray();

        if (siblings.Length == 0) return [];

        var result = Types.InAssembly(AssemblyOf(Web))
            .That().ResideInNamespace($"{Web}.Features.{slice}")
            .ShouldNot().HaveDependencyOnAny(siblings)
            .GetResult();

        return Violations(result);
    }

    /// <summary>
    /// Directories under <c>Features/</c> that are CROSS-CUTTING infrastructure rather than peer
    /// feature slices: any slice may depend on them, and doing so is not a boundary violation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Shared</c> is the deliberate common vocabulary (<c>AuthorizationPolicies</c>,
    /// <c>AntiforgeryExtensions</c>).
    /// </para>
    /// <para>
    /// <c>Idempotency</c> earns the same treatment on evidence, not convenience: its public surface
    /// is <c>IdempotencyKeyFilter</c>, an <c>IEndpointFilter</c> registered globally in
    /// <c>Program.cs</c> and attached by slices that perform writes — structurally identical to the
    /// antiforgery filter that already lives in <c>Shared</c>. Enforcing it as a peer slice would
    /// flag every write endpoint in the app, which measures directory layout rather than coupling.
    /// Worth noting the smell though: it sits under <c>Features/</c> while behaving like
    /// infrastructure, so moving it beside <c>Shared</c> would make the layout match the design.
    /// </para>
    /// </remarks>
    private static readonly string[] CrossCutting = ["Shared", "Idempotency"];

    /// <summary>
    /// Slices whose isolation is enforced. Every directory under <c>Features/</c> is a source here
    /// — <c>Idempotency</c> included, so cross-cutting status never becomes a licence for it to
    /// reach back into a feature — but the entries in <see cref="CrossCutting"/> are not counted as
    /// forbidden targets.
    /// </summary>
    private static readonly string[] Slices =
    [
        "Auth", "BulkGenerate", "Diagnostics", "Idempotency", "ImageAnalysis",
        "MemeTemplates", "Pricing", "RapRoast", "StyleDirector", "UserImages"
    ];

    public static IReadOnlyList<ArchitectureRule> All { get; } = BuildAll();

    private static ArchitectureRule[] BuildAll()
    {
        var layering = new ArchitectureRule[]
        {
            new("domain-no-infrastructure", "Core isolation",
                "Domain must not depend on Infrastructure",
                () => Forbid(Domain, Domain, Infrastructure)),

            new("domain-no-web", "Core isolation",
                "Domain must not depend on the Web/BFF host",
                () => Forbid(Domain, Domain, Web)),

            new("domain-no-client", "Core isolation",
                "Domain must not depend on the Blazor client",
                () => Forbid(Domain, Domain, Client)),

            new("domain-no-application", "Core isolation",
                "Domain must not depend on Application (dependencies point inward)",
                () => Forbid(Domain, Domain, Application)),

            new("domain-no-aspnetcore", "Core isolation",
                "Domain must not depend on ASP.NET Core (stays a plain library)",
                () => Forbid(Domain, Domain, "Microsoft.AspNetCore")),

            new("domain-no-persistence", "Persistence isolation",
                "Domain must not depend on Azure storage/SDK types",
                () => Forbid(Domain, Domain, "Azure.Data.Tables", "Azure.Storage")),

            new("application-no-infrastructure", "Layer boundaries",
                "Application must not depend on Infrastructure (depends on Domain abstractions)",
                () => Forbid(Application, Application, Infrastructure)),

            new("application-no-web", "Layer boundaries",
                "Application must not depend on the Web/BFF host",
                () => Forbid(Application, Application, Web)),

            new("application-no-client", "Layer boundaries",
                "Application must not depend on the Blazor client",
                () => Forbid(Application, Application, Client)),

            new("infrastructure-no-web", "Layer boundaries",
                "Infrastructure must not depend on the Web/BFF host",
                () => Forbid(Infrastructure, Infrastructure, Web)),

            new("infrastructure-no-client", "Layer boundaries",
                "Infrastructure must not depend on the Blazor client",
                () => Forbid(Infrastructure, Infrastructure, Client)),

            new("shared-no-server-layers", "Contract isolation",
                "Shared (the WASM/API contract assembly) must not depend on server-only layers",
                () => Forbid(Shared, Shared, Application, Infrastructure, Web)),

            new("shared-no-client", "Contract isolation",
                "Shared must not depend on the Blazor client",
                () => Forbid(Shared, Shared, Client)),

            new("shared-no-persistence", "Contract isolation",
                "Shared must stay trim-safe: no Azure storage/SDK dependencies",
                () => Forbid(Shared, Shared, "Azure.Data.Tables", "Azure.Storage")),

            new("client-no-infrastructure", "Browser boundary",
                "Client must not depend on Infrastructure (server code must never ship to the browser)",
                () => Forbid(Client, Client, Infrastructure)),

            new("client-no-web", "Browser boundary",
                "Client must not depend on the Web/BFF host assembly",
                () => Forbid(Client, Client, Web)),

            new("client-no-persistence", "Browser boundary",
                "Client must not depend on Azure storage/SDK types",
                () => Forbid(Client, Client, "Azure.Data.Tables", "Azure.Storage", "Azure.Identity")),
        };

        var sliceRules = Slices.Select(slice => new ArchitectureRule(
            $"slice-isolation-{slice.ToLowerInvariant()}",
            "Vertical slice encapsulation",
            $"Feature slice '{slice}' must not depend on a sibling slice",
            () => SliceIsolation(slice, Slices)));

        return [.. layering, .. sliceRules];
    }
}
