using System.Net.Http.Json;
using PoRedoImage.Shared.Configuration;
using PoRedoImage.Shared.DTOs;
using PoRedoImage.Shared.Json;

namespace PoRedoImage.Client.Models;

/// <summary>
/// The session's per-capability provider choices.
/// </summary>
/// <remarks>
/// Registered scoped, which in Blazor WASM lasts the lifetime of the app instance: selections
/// survive navigation between Studio and the feature pages but reset on reload. That is deliberate —
/// nothing persists to storage, so a removed provider can never leave a stale selection behind.
/// It must be a service rather than page state because Studio is where the user chooses but the
/// feature pages are where requests are issued.
/// </remarks>
public sealed class AiSelectionState(HttpClient http)
{
    private readonly Dictionary<AiCapability, string> _selections = [];

    /// <summary>
    /// The image-generation provider id seeded from the server's configured provider (see
    /// <see cref="EnsureInitializedAsync"/>), or null when seeding has not run yet or failed.
    /// Deliberately narrow to one capability — image generation is the only one where an
    /// unreachable-by-construction config flag was the defect this seeding exists to fix.
    /// </summary>
    private string? _seededImageGenProviderId;

    /// <summary>
    /// Caches the in-flight/completed seed so <see cref="EnsureInitializedAsync"/> fetches at most
    /// once per instance no matter how many components call it. Assigned synchronously on the first
    /// call (before any <c>await</c> inside <see cref="SeedImageGenDefaultAsync"/> runs), so a second
    /// caller arriving before the first completes observes the same task rather than racing a second
    /// HTTP request. The task itself never faults — every failure path is swallowed inside
    /// <see cref="SeedImageGenDefaultAsync"/> — so callers never need a try/catch around the await.
    /// </summary>
    private Task? _ensureInitializedTask;

    /// <summary>Raised after any selection changes so open components can re-render.</summary>
    public event Action? OnChange;

    /// <summary>The selected provider id, falling back to the seeded or catalog default.</summary>
    public string Get(AiCapability capability) =>
        _selections.TryGetValue(capability, out var id) ? id : DefaultId(capability);

    /// <summary>The selected option, falling back to the catalog default when the id is unknown.</summary>
    public AiProviderOption GetOption(AiCapability capability) =>
        AiServiceCatalog.Find(capability, Get(capability)) ?? AiServiceCatalog.DefaultFor(capability);

    /// <summary>Records a selection and notifies subscribers.</summary>
    public void Set(AiCapability capability, string providerId)
    {
        if (Get(capability) == providerId) return;

        _selections[capability] = providerId;
        OnChange?.Invoke();
    }

    /// <summary>
    /// The user's explicit choice for <paramref name="capability"/>, falling back to the seeded
    /// default established by <see cref="EnsureInitializedAsync"/> — or <c>null</c> when neither
    /// exists. Unlike <see cref="Get"/>, this never falls back to the catalog default: a caller that
    /// stamps this straight onto a request needs to know the difference between "we are confident in
    /// this provider" and "we have no idea," because the server-side router treats <c>null</c> as
    /// "use your own configured default" rather than a guess this client is not entitled to make.
    /// </summary>
    public string? GetExplicit(AiCapability capability)
    {
        if (_selections.TryGetValue(capability, out var id)) return id;
        return capability == AiCapability.GenerateImage ? _seededImageGenProviderId : null;
    }

    /// <summary>
    /// How long the seed fetch is allowed to run before it is abandoned in favour of degrading to
    /// "no seeded default." <see cref="HttpClient.Timeout"/> defaults to 100 seconds (often
    /// overridden higher for slow local-model endpoints elsewhere in this app), which would otherwise
    /// let a black-holed <c>/api/pricing</c> call stall the request-build critical path for minutes
    /// with no cancel affordance. This budget is internal to the seed fetch only — it does not affect
    /// any other use of <paramref name="http"/>.
    /// </summary>
    private static readonly TimeSpan SeedBudget = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Seeds the image-generation default from the server's actually-configured provider
    /// (<c>GET api/pricing</c>). Idempotent — safe to call from multiple components; the first
    /// caller performs the fetch and every caller (concurrent or later) awaits the same result.
    /// Never throws: any failure (network, non-success status, an unrecognised provider key, or the
    /// internal <see cref="SeedBudget"/> expiring) leaves the seeded default at <c>null</c> rather
    /// than guessing.
    /// </summary>
    public Task EnsureInitializedAsync(CancellationToken ct = default) =>
        _ensureInitializedTask ??= SeedImageGenDefaultAsync(ct);

    private string DefaultId(AiCapability capability) =>
        capability == AiCapability.GenerateImage && _seededImageGenProviderId is { } seeded
            ? seeded
            : AiServiceCatalog.DefaultFor(capability).Id;

    private async Task SeedImageGenDefaultAsync(CancellationToken ct)
    {
        try
        {
            // Bound the fetch to SeedBudget regardless of the first caller's own token, so a slow or
            // black-holed /api/pricing can never dominate this task's caller(s) for the full
            // HttpClient.Timeout. Deliberately NOT applied via `.WaitAsync(ct)` on the cached task
            // (that would surface as an unwrapped OperationCanceledException to callers, which the
            // feature pages would mislabel as a first-run local-model download); the timeout lives
            // entirely inside this try, so the existing catch below still swallows it into the same
            // "seeding failed" degrade-to-null path as every other failure mode.
            using var budget = new CancellationTokenSource(SeedBudget);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, budget.Token);
            var pricing = await http.GetFromJsonAsync<AiPricingDto>("api/pricing", SharedJsonOptions.Default, linked.Token);
            _seededImageGenProviderId = pricing?.ImageProvider switch
            {
                // 2026-07: HuggingFace fal-ai image generation is broken on the upstream provider,
                // so a "huggingface" price key is intentionally NOT mapped to any image-gen id —
                // falling through to null lets the server-side router use its own configured
                // default (which itself is pinned to Gemini).
                "google" => AiProviderIds.GeminiImagen3,
                _ => null, // Unrecognised provider key — do not guess.
            };
        }
        catch (Exception)
        {
            // Network error, non-success status, or a response that failed to deserialize. This is
            // the important half of the fix: degrade to "no seeded default" (GetExplicit returns
            // null, and the server-side router falls back to its own ImageGen:Provider config) —
            // never fabricate a provider id we are not confident in.
            _seededImageGenProviderId = null;
        }
    }
}
