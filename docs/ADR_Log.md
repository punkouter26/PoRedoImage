---
project: PoRedoImage
tier: 3
type: registry
last_updated: 2026-06-01
format: "ADR (Architecture Decision Record)"
---

# ADR Log — PoRedoImage

> Why certain technologies or patterns were chosen. Bulleted for zero-waste AI token efficiency.

---

## ADR-001: Blazor Web App — Interactive WebAssembly Only (no SSR / no Interactive Server)

- **Decision:** Use .NET 10 Blazor Web App with `AddInteractiveWebAssemblyComponents()` + global `InteractiveWebAssembly` render mode. Pre-rendering is disabled (`RenderModes.WasmNoPrerender`).
- **Why:** Single language (C#) across the full stack, no second build system, the API host owns the BFF and serializes the cookie-authenticated principal (claims only, never tokens) into the WASM client via `AddAuthenticationStateSerialization`/`AddAuthenticationStateDeserialization`. SSR + Interactive Server were evaluated and rejected for this app: SSR pre-render would re-issue cookie auth + KV calls per request, eroding the BFF invariant, and Interactive Server requires a persistent SignalR channel that conflicts with the F1 cold-start budget (ADR-015).
- **Alternatives:** React SPA + .NET API (rejected: two languages, two build systems), Blazor United with SSR (rejected: token-in-browser risk + extra render cost), Blazor Server only (rejected: stateful, blocks F1 cold-starts).
- **Trade-off:** Larger initial bundle than SSR, but the bundle is small (no SSR markup payload) and the BFF invariant is preserved.
- **Revisit when:** a feature genuinely needs pre-rendered HTML for SEO or first-paint, or the F1 plan is replaced with Always On.

## ADR-002: Minimal APIs (No MVC Controllers)

- **Decision:** All endpoints use `MapGroup` + static handler methods. No `Controller` classes.
- **Why:** Vertical Slice Architecture — each feature owns its endpoint + logic co-located. Reduces ceremony, improves token efficiency.
- **Alternatives:** MVC controllers with `[ApiController]` (rejected: ceremony, routing overhead).
- **Trade-off:** Less "standard" for .NET devs familiar with MVC; mitigated by consistent pattern.

## ADR-003: Azure Computer Vision + OpenAI + Gemini (Multi-AI)

- **Decision:** Chain Azure CV (vision) → Azure OpenAI `gpt-5.4-nano` chat deployment (language) → Google Gemini Imagen3 (generation).
- **Why:** Each AI service excels at its domain. CV for tagging, OpenAI for description enhancement, Gemini for image generation.
- **Source of truth:** deployment name is set in `infra/main.bicep` as the literal `OpenAI__ChatCompletionsDeployment` app setting (NOT a Key Vault reference — KV reference caching previously returned a stale `gpt-4.1-nano` and produced 404 `DeploymentNotFound` at first call). The C# code default in `Configuration/OpenAiOptions.cs` and the dev `appsettings.json` value both match this name.
- **Alternatives:** Single-provider (e.g., all OpenAI): rejected due to Gemini's superior image-to-image quality.
- **Trade-off:** Three API keys, three health checks; mitigated by Key Vault + health check endpoints.

## ADR-004: Azure Table Storage + Blob Storage (Dual Storage)

- **Decision:** Table Storage for entities (UserImage metadata, BulkPrompts). Blob Storage for raw image bytes.
- **Why:** Table Storage is cheap, fast for metadata queries. Blob Storage handles large binary efficiently. Separation keeps costs low.
- **Alternatives:** Cosmos DB (rejected: overkill for current scale, 3× cost).
- **Trade-off:** Two storage backends to manage; mitigated by repository pattern.

## ADR-005: Key Vault with 30-Minute Reload

- **Decision:** Load secrets from Azure Key Vault with `ReloadInterval = 30 min` and `KeyVaultSecretNameMapping`.
- **Why:** Zero-downtime secret rotation. No app restart needed when keys rotate.
- **Alternatives:** App Settings only (rejected: no rotation, manual restart required).
- **Trade-off:** 30-min window where stale secrets could cause failures; mitigated by health checks.

## ADR-006: Result<T,E> Discriminated Union — Deferred

- **Decision (initial, 2026-04):** Use `Result<T, E>` struct instead of null returns or exceptions for expected failures.
- **Why:** Eliminates silent no-ops (Po2Logic Failure #9). Forces callers to handle both success and error paths.
- **Alternatives:** Nullable returns (rejected: hidden failures), exceptions (rejected: expensive for expected cases).
- **Trade-off:** More verbose call sites; mitigated by `Match()` pattern.
- **Status (2026-06):** **Removed.** The `Result<T, E>` type and its `StorageError` enum existed in `PoRedoImage.Domain/Result.cs` but had zero consumers. YAGNI — reintroduce when an actual repository/service needs to surface a typed error to a caller. The repositories today log and return `null` / `[]` on storage unavailability, which is acceptable for a single-tenant hobby workload; revisit if the multi-tenant path becomes real.

## ADR-007: Idempotency via IEndpointFilter

- **Decision:** `IdempotencyKeyFilter` registered as a scoped `IEndpointFilter` and applied via `AddEndpointFilter<IdempotencyKeyFilter>()` on the user-image and bulk-generate endpoint groups. Backed by `IMemoryCache` with 24h TTL.
- **Note:** The companion `[Idempotent]` marker attribute (originally part of this ADR) was **removed in 2026-06** because no endpoint ever applied it — the filter is wired explicitly per group instead. Reintroduce the attribute if a future feature benefits from declarative opt-in.
- **Why:** Prevents duplicate writes from network retries (Po2Logic F6). 24h TTL prevents replays across days.
- **Alternatives:** Client-side dedup (rejected: unreliable), database constraints only (rejected: late detection).
- **Trade-off:** Memory pressure from cached keys; mitigated by TTL eviction.

## ADR-008: Agentic Style Director (4-Agent Sequential)

- **Decision:** Implement Idea #1 as a 4-agent sequential workflow: VisionAnalyst → StyleStrategist → PromptRefiner → Critic.
- **Why:** Explainable AI — each agent produces a reasoning entry visible in the UI. Separation of concerns.
- **Alternatives:** Single monolithic prompt (rejected: no explainability), parallel agents (rejected: sequential dependency).
- **Trade-off:** Higher latency (4 sequential calls); mitigated by streaming progress to UI.

## ADR-009: SkiaSharp for Meme Overlay (Not ImageSharp)

- **Decision:** Use SkiaSharp for meme text overlay, ImageSharp for meme generation service.
- **Why:** SkiaSharp provides fine-grained text layout control (font size, alignment, wrapping). ImageSharp is used for the broader meme template system.
- **Alternatives:** HTML canvas overlay (rejected: server-side only), FFmpeg (rejected: overkill for text overlay).
- **Trade-off:** Two image libraries; justified by different use cases (text layout vs. template management).

## ADR-010: OIDC with Zero-Secret Deploy

- **Decision:** Production uses Microsoft Entra ID OIDC with `ResponseType = "code"`. Dev uses cookie-only auth.
- **Why:** Zero-secret deployment — OIDC code flow doesn't require client secrets in production. Dev bypass for rapid iteration.
- **Alternatives:** Client credentials flow (rejected: requires secret management), no auth (rejected: security).
- **Trade-off:** Dev/prod auth divergence; mitigated by `AuthServiceExtensions` conditional registration.

## ADR-011: Radzen Blazor Component Library

- **Decision:** Use Radzen Blazor components for UI (Button, Card, Tabs, Upload, ProgressBar, etc.).
- **Why:** Mature Blazor component library with dark theme support. Reduces custom CSS. Bento Box layout compatible.
- **Alternatives:** MudBlazor (rejected: heavier customization overhead), Syncfusion (rejected: licensing cost).
- **Trade-off:** Vendor dependency; mitigated by Radzen's open-source core.

## ADR-012: Serilog + OpenTelemetry → Application Insights

- **Decision:** Structured logging via Serilog (Console + File + App Insights). Traces/metrics via OpenTelemetry → Azure Monitor.
- **Why:** Dual telemetry: Serilog for structured logs, OTel for distributed traces. No OTLP collector needed.
- **Alternatives:** NLog only (rejected: no OTel integration), pure OTel (rejected: no structured logging).
- **Trade-off:** Two telemetry pipelines; justified by complementary capabilities.
## ADR-013: Two-Tier Test Layout (Unit/Integration + E2E) — Superseded by ADR-022

- **Decision (original):** Consolidate the four initial test projects (Tests.Unit, Tests.Integration, Tests.E2EAPI, Tests.E2EUI) into three: Tests.Unit, Tests.Integration, Tests.E2E. The latter merges HTTP smoke + C# Playwright browser tests under one LiveServerFactAttribute.
- **Why:** Single base-URL resolver, single attribute, single fixture graph. The duplicate LiveServerFactAttribute (byte-for-byte the same in both E2E projects) was a known smell.
- **Status (2026-06):** **Superseded by ADR-022.** The user's project spec mandates four test projects (`Tests.Unit`, `Tests.Integration`, `Tests.E2E.ApiSmoke`, `Tests.E2E.UI`) — not the three we landed on. The ApiSmoke + UI split is now in place (see ADR-022). The remaining Tests.Unit + Tests.Integration split mirrors test-runner conventions (Testcontainers-backed tests vs. pure logic tests) and is intentional — combining them would force unit tests to take an IClassFixture<WebApplicationFactory> even when they don't need one.
- **Trade-off (revisited):** Four test projects, with `Tests.E2E.UI` (Playwright) a separate compile target from `Tests.E2E.ApiSmoke` (pure HTTP). The duplicate-LiveServerFactAttribute smell is resolved by defining the attribute once in `ApiSmoke` and `<ProjectReference>`-ing it from `UI`.

## ADR-014: Shared References Domain — Intentional

- **Decision:** PoRedoImage.Shared has a project reference to PoRedoImage.Domain. The shared DTOs re-use UserImageKind, CaptionPersona, and MemeTemplate from Domain so the wire contract and the domain contract share enum values without copy-paste drift.
- **Why:** A leaf-Shared rule would force every cross-wire enum to live in two places (Domain + Shared), with mapping extensions at the endpoint boundary. The mapping cost is real (10+ mapping sites) for a benefit that doesn't show up in any user-facing behaviour.
- **Alternatives considered:** Move UserImageKind and CaptionPersona to Shared (rejected: Domain would then need to reference Shared to use the enums in entities — circular). Introduce a third "Enums" project (rejected: extra project overhead for two small enums).
- **Trade-off:** Shared is not a leaf. Acceptable because the Shared surface is genuinely DTOs; the Domain enums are the source of truth.

## ADR-017: `/health` Smoke Test Accepts `Degraded`, Rejects `Unhealthy`

- **Decision:** The post-deploy smoke test in `.github/workflows/deploy.yml` requires HTTP 200 from `/health` with **no `Unhealthy` entries**; `Degraded` entries are accepted and logged for follow-up. Concurrently, the four named readiness checks (`key-vault`, `openai`, `computer-vision`, `table-storage`) are taught to return `Degraded` (not `Unhealthy`) when their configuration is absent — and to surface a remediation hint pointing at the App Service Key Vault reference + managed-identity chain — so the smoke test can distinguish "deploy broke the app" from "ops has a KV reference to fix".
- **Why:** On 2026-06-28 the smoke test failed after a deploy that **was correct**; the real problem was that an App Service Key Vault reference (`@Microsoft.KeyVault(...)`) had not resolved at runtime (missing secret, role not propagated, or vault-side issue), producing empty strings for `OpenAI:Endpoint`, `ComputerVision:Endpoint`, `Storage:ConnectionString`, and `AZURE_KEY_VAULT_ENDPOINT`. The previous checks caught the empty strings and bubbled them up as `Unhealthy`, which the smoke test treated as a deploy failure and aborted. The deploy itself was fine — a separate Azure-side fix is needed (create the missing secret or grant the role). Treating `Degraded` as a hard fail conflates a deploy problem with an ops problem.
- **Mapping** (severity → check status → smoke test):
  - HTTP 200 + all `Healthy` → deploy + KV/MI end-to-end OK.
  - HTTP 200 + some `Degraded` (config missing) → deploy OK; investigate per-check description for the specific KV reference / app setting.
  - HTTP 503 + any `Unhealthy` → real bug (probe threw, exception message in body). Hard fail.
  - HTTP 000/timeout → app did not start. Hard fail.
- **Alternatives considered:** Relax the smoke test to "HTTP 200 only" (rejected: would mask probe exceptions like the historic `InvalidOperationException: An invalid request URI was provided` bug). Add a separate `/diag` endpoint that lists raw `IConfiguration["..."]` values (rejected: leaks secrets, and the existing admin-gated `/api/diag` was already removed). Fail the deploy when any check is `Degraded` (rejected: blocks deploys on every transient KV propagation delay, which the F1 plan is especially sensitive to).
- **Revisit when:** the App Service platform guarantees synchronous KV reference resolution before the first user request (currently it is best-effort), or when the readiness checks themselves are upgraded to fail-fast on missing config in Production via `IValidateOptions<T>`.

## ADR-016: Start App Service Before `azure/webapps-deploy@v3`

- **Decision:** The `deploy` job in `.github/workflows/deploy.yml` runs an `Ensure App Service is running` step **before** `azure/webapps-deploy@v3`. The step queries `state`, calls `az webapp start` if not `Running`, polls up to ~60s for the transition, and hard-fails the pipeline if the site never reaches `Running`.
- **Why:** `azure/webapps-deploy@v3` uses OneDeploy, which returns `403 Site Disabled` when the App Service is `Stopped`. Two paths can leave the site Stopped: a previously failed deploy (the prior failed run never restarted it) and a manual stop via the Azure portal. The bicep apply that runs earlier in the same job is idempotent — it creates the site on first run but does **not** restart an already-stopped site on subsequent runs. Without the explicit start, OneDeploy 403s and the deploy fails even though nothing about the package is wrong.
- **Alternatives considered:** Add `properties.state = 'Running'` to `Microsoft.Web/sites` in `infra/main.bicep` (rejected: that property does not exist on the ARM resource — runtime state is set exclusively via `az webapp start` / `az webapp stop` or the portal). Set `WEBSITE_STARTUP_AS_PAGE_PROCESS` and hope for auto-start (rejected: not a real mechanism). Switch to zip-deploy via `azure/appservice-zip-deploy` (rejected: OneDeploy is the supported action and adds package validation we want).
- **Trade-off:** Adds one CLI call + up to 60s of polling per deploy. Mitigated by the early no-op fast path (`Running` → skip the `start` + loop) and by the existing post-deploy health-smoke test that already tolerates the first cold start.
- **Revisit when:** Microsoft changes OneDeploy to accept Stopped sites, or when we move off F1 Free onto a tier with Always On (no Stopped state possible).

## ADR-015: F1 Free App Service Plan — Cold Starts Accepted

- **Decision:** Host the web app on the F1 Free Linux plan (`asp-poredoimage-f1`). Accept cold starts as the cost of the Free tier; do **not** add a keep-warm pinger.
- **Why:** This is a low-traffic personal app. F1 is $0/month. The alternative (B1 Basic, ~$13/month, supports Always On) buys warm starts the traffic does not justify.
- **Constraints that force the trade-off:** F1 cannot enable Always On (`alwaysOn: false` is mandatory in `infra/main.bicep` or the deploy fails) and caps CPU at 60 min/day. A scheduled keep-warm ping would burn into that 60-min budget and could exhaust it, so it is explicitly rejected — a pinger would trade cold starts for hard CPU-quota throttling, which is worse.
- **Mitigations already in place:** `WEBSITE_CONTAINER_START_TIME_LIMIT=600` allows slow first-token cold starts to complete; the post-deploy `/health` smoke test retries 5× with back-off to ride out the first cold start; Key Vault references populate config at platform level so the first request doesn't wait on the in-app KV provider.
- **Revisit when:** sustained traffic makes cold-start latency a real UX complaint, or a feature needs background processing — at which point move to B1 (Always On) rather than papering over F1 with a pinger. The plan binding is asserted post-deploy in CI, so an accidental tier change is caught.

## ADR-018: Resource Naming — `po`-prefixed lowercase (not strict `Po{SolutionName}`)

- **Decision:** All Azure resources are named with a lowercase `po…` token (`poredoimage-web`, `asp-poredoimage-f1`, `stporedoimage26`, `kv-poshared`) and live in the `PoRedoImage` or `PoShared` resource groups. The governance convention enforced by `SCRIPTS/audit-arg.ps1` is the `^po` prefix, **not** PascalCase `Po{SolutionName}`.
- **Why:** Most Azure resource types (Storage accounts, Web apps, Key Vaults) require globally-unique, DNS-safe, lowercase names — PascalCase `PoRedoImage` is simply illegal for them. So a strict `Po{SolutionName}` rule is unenforceable; codifying the *real* rule (lowercase `po` prefix) makes the audit meaningful instead of perpetually red.
- **Enforcement:** `SCRIPTS/audit-arg.ps1` runs three Azure Resource Graph queries — stray resources outside the two owned RGs, names that don't start with `po`/`Po`, and idle compute (<5% CPU/7d). It is **report-only** and runs locally or as a *separate* scheduled workflow, never in the deploy pipeline (project policy: deploy.yml builds + ships only).
- **Trade-off:** Resource-group names keep PascalCase (`PoRedoImage`, `PoShared`) since RGs allow it; only resources inside follow the lowercase rule. Two casings, but each matches what its scope actually permits.

## ADR-019: Storage Lifecycle + Testcontainers-Only Test Storage

- **Decision (lifecycle):** `infra/main.bicep` defines a blob management policy that tiers block blobs to Cool after 7 days and **deletes them after 30 days**, plus a 7-day blob soft-delete window. Generated images and Kudu/app log blobs are regenerable, so nothing needs to persist indefinitely.
- **Why:** Previously nothing expired, so generated-image and log blobs grew unbounded — the clearest cloud-cost leak. A 30-day delete + cool-tiering caps cost while keeping a comfortable recovery window.
- **Decision (test storage):** The Integration tier spins up its own **ephemeral Azurite via Testcontainers** inside the `WebApplicationFactory` lifecycle (`AzuriteContainerFixture`). The `docker-compose.yml` Azurite container is for the local dev inner loop (F5) ONLY and now uses a Docker-managed named volume, not the old `./.azurite-data` repo bind-mount.
- **Why:** One storage path per purpose. The old bind-mount committed emulator state into the working tree and let stale tables/blobs survive across runs, which could leak into or flake tests. Tests owning their own throwaway container removes that class of failure.

## ADR-020: Telemetry Budget — Explicit Sampling, No Keep-Warm Availability Test

- **Decision:** The production App Insights sampling ratio (0.1) is set **explicitly** as `ApplicationInsights__SamplingRatio` in `infra/main.bicep` (not left to the code default), the `ErrorPreservingSampler` keeps all error spans, Serilog exports exceptions at 100%, and metrics/heartbeat are unaffected by the trace sampler. No availability/keep-warm web test is added.
- **Why:** "Aggressive sampling, retain heartbeat + exceptions, drop noise." Making the ratio explicit in IaC keeps the budget auditable in the portal. An availability web-test would repeatedly wake the F1 app and burn its 60-min/day CPU budget (conflicts with ADR-015), so up/down visibility relies on the SDK heartbeat metric instead.

## ADR-021: AI Mock Boundary — DI Swap + HTTP Handler (Defense in Depth)

- **Decision:** When `Mocks:UseMockAi=true`, AI services are swapped for zero-network mocks at DI registration AND a `MockAiDelegatingHandler` sits on the outbound AI named HTTP clients (`GeminiApi`, `Ollama`). The handler throws (fails loud) if a real AI call is attempted while mock mode is on.
- **Why:** The DI swap already guarantees zero token spend in normal operation, but a future regression (a real service wired while the flag is on) would silently spend tokens. The HTTP-pipeline handler is a second wall that blocks the call instead of masking the misconfiguration with fake data. Belt and suspenders for the "zero token spend" budget guarantee, reinforced by the E2E `Ai_services_are_mocked_when_mock_mode_is_required` pre-flight check.

## ADR-022: Four-Project E2E Split (ApiSmoke + UI)

- **Decision:** The end-to-end test suite is split into two projects, matching the user spec's four-project rule (`Tests.Unit`, `Tests.Integration`, `Tests.E2E.ApiSmoke`, `Tests.E2E.UI`). The previous merged `Tests.E2E` is gone. `Tests.E2E.ApiSmoke` is the source of truth for the shared `LiveServerFactAttribute` + `E2EApiFixture`; `Tests.E2E.UI` references it via `<ProjectReference>`.
- **Why:**
  - **Independent test agents:** ApiSmoke runs on any CI agent without the Playwright/Chromium cache dependency. UI opts into the slower `pwsh playwright.ps1 install chromium` step only when a browser run is queued.
  - **Faster signal:** an ApiSmoke failure (HTTP contract, auth, /health) surfaces separately from a UI failure (layout, viewport, hydration), so triage is faster.
  - **No token spend on UI runs:** the API smoke budget guardrail (`Ai_services_are_mocked_when_mock_mode_is_required`) lives in ApiSmoke; UI can run against a real-config build without worrying about budget exposure.
  - **Single source of truth for the live-server probe:** `LiveServerFactAttribute` and the shared `E2EApiFixture` are defined once in ApiSmoke and `<ProjectReference>`-d by UI — the previous byte-for-byte duplicate is eliminated.
- **Per-tier ceilings:** both projects independently enforce the 25-method cap (was previously 25 for the merged E2E). The `Contains("Ui")` convention is no longer needed because each assembly contains exactly one tier.
- **Revisit when:** a third E2E tier appears (e.g. long-running soak tests) — at which point add a third project and re-evaluate the LiveServerFact base-URL resolver to share via a common test-utility project instead of ApiSmoke.

## ADR-023: Key Vault Role Assignment — Inline `az role assignment create` (No Bicep Module)

- **Decision:** The post-deploy "grant the App Service managed identity `Key Vault Secrets User` on `kv-poshared`" step is implemented as a direct `az role assignment create` call in `.github/workflows/deploy.yml`, NOT as a Bicep deployment.
- **Status (2026-06):** Reverts the Bicep-module approach introduced in the "Top 10 ideas" refactor (infra/kv-role.bicep + infra/modules/kv-role-assignment.bicep). The Bicep files have been deleted.
- **Why (revert from Bicep):** The CI service principal (`5d920e19-04d8-4772-8a22-74072c5038ff`) has `Microsoft.Authorization/roleAssignments/write` on the `PoShared` resource group (which is what `az role assignment create` needs) but does **not** have `Microsoft.Resources/deployments/validate/action` at the subscription scope (which is what `az deployment sub create` needs). A subscription-scoped Bicep deployment hard-failed in PR #51 run 28381505637 with `AuthorizationFailed: ... does not have authorization to perform action 'Microsoft.Resources/deployments/validate/action' over scope '/subscriptions/...'`.
- **Why not grant subscription-level RBAC to the SP:** That would be a privilege escalation — the SP only needs the narrow `Microsoft.Authorization/roleAssignments/write` permission, which it already has on `PoShared`. Granting `Microsoft.Resources/deployments/*` at the subscription scope would let a compromised CI pipeline spin up arbitrary resources in **any** RG across the subscription, not just PoRedoImage and PoShared.
- **Why not a resource-group-scoped Bicep deployment (`az deployment group create --resource-group PoShared`):** The SP similarly doesn't have the deployment RBAC on `PoShared` (only on `PoRedoImage`). Same permission issue, different scope.
- **Idempotency guarantees preserved:** The `az role assignment create` call uses a stable GUID name (`guid(vaultId, principalId, roleId)` would be ideal but the CLI doesn't expose this directly; the existing pattern uses a `grep -v "AlreadyExists"` filter on the 409 response) and the post-assign `az role assignment list` query verifies the binding is in place. Re-runs converge to a single assignment, never duplicates.
- **What "good" looks like, eventually:** When the SP is upgraded to a custom role that includes `Microsoft.Resources/deployments/validate/action` on both RGs, the Bicep approach becomes viable again. Until then, the inline CLI is the right tool — it needs less permission, has fewer moving parts, and the same idempotency properties.
- **Revisit when:** The CI SP's RBAC is restructured to include deployment permissions on the shared resource group, OR when the role assignment moves to a resource group the SP already has deployment access to.

## ADR-024: Caption Battle Removed (User Decision, 2026-06-29)

- **Decision:** The Meme Caption Battle feature (Idea #5) is removed in its entirety — domain interface, domain entity, service, mock, endpoint, DTOs, JSON-serializable registrations, the entire MemeGeneration.razor card + state + handlers, the StyleDirector/openai `[GenerateCaptionAsync]` method, and the 7 unit tests.
- **Why:** The feature was a parallel-fanout of 8 OpenAI chat completions per analysis. In practice the user-visible behavior was poor:
  - 4 of 8 personas consistently hit `HTTP 429 too_many_requests` on `gpt-5.4-nano` (shared TPM) and surfaced as "skipped" cards — visible in the screenshot that triggered the removal.
  - The full battle took **152 seconds** end-to-end (rate-limited retries with up to 60s waits per request). The user had to wait ~2.5 minutes for what was supposed to be a "3–6 second" interaction.
  - The two usable cards (Gen-Z, Absurdist) were essentially the same caption rephrased; the persona prompts diverged less than expected and the cost/benefit was poor (8× tokens for ~2× unique insight).
  - The mock-mode overshadowing made the UI look like a placeholder when running locally with `Mocks:UseMockAi=true`.
- **What was removed (6 files + 7 file edits):**
  - Files: `Domain/Entities/CaptionPersona.cs`, `Domain/Interfaces/ICaptionBattleService.cs`, `Infrastructure/Services/CaptionBattleService.cs`, `Shared/DTOs/CaptionBattleDtos.cs`, `Web/Features/CaptionBattle/CaptionBattleEndpoints.cs` + folder, `Tests.Unit/Features/CaptionBattleServiceTests.cs`.
  - Edits: `IGenerativeAiService` and `AzureOpenAiService` lose `GenerateCaptionAsync`; `MockGenerativeAiService` loses its caption override; `InfrastructureServiceExtensions` loses the singleton registration; `Program.cs` loses the `using` + `MapCaptionBattleEndpoints()`; `SharedJsonContext` loses the two `[JsonSerializable]` attributes; `MemeGeneration.razor` loses the card + state + `StartCaptionBattle` + `VoteForCaption` + the reset line; `ImageAnalysisEndpointTests.ReplaceService` doc comment loses the `ICaptionBattleService is a singleton` reference; `AGENT.MD` API table loses the row.
- **Build/test state:** 0 warnings, 0 errors; unit tests went from 103 → 96 (the 7 caption-battle tests are gone, the rest are unchanged and green).
- **What this is NOT:** This is not a judgment that the persona-based caption generator is a bad idea. It's a judgment that the cost (8× token spend, 152s latency, 4-of-8 typical failure rate under shared TPM) is too high for a hobby workload, and the UI value (vote + adapt voice) was never actually implemented — the winner just changed a `__winningPersona` field that nothing else consumed.
- **Revisit when:** (a) per-user rate-limit budget is allocated (a dedicated `gpt-5.4-nano` deployment with enough TPM to absorb 8 concurrent calls), OR (b) the persona contrast is redesigned to use a single, structured JSON response with all 8 personas (1 OpenAI call instead of 8), OR (c) the winner-feedback loop is actually wired into the "regenerate" path so the user sees a tangible benefit from picking a winner.

## ADR-025: Move Off F1 Free Tier — F1 CPU Quota Burned, 2026-06-29

- **Decision:** The web app (`poredoimage-web`) is moved from the F1 Free Linux App Service Plan (`asp-poredoimage-f1`) to the Basic B1 plan (`asp-poredoimage-b1`) in the same resource group. The Bicep default flips from `F1` to `B1` (`appServicePlanSkuName` param), and the workflow's `EXPECTED_PLAN` env updates to match.
- **Why:** F1 caps daily CPU at 60 minutes wall-clock across the whole plan. A single dev-day of activity exceeded the quota:
  - 6+ cold starts (each ~60s on F1 cold-start) on the post-deploy smoke test
  - The 152-second caption-battle request that hit OpenAI rate limits (the 8 parallel fanout held the CPU)
  - The post-deploy OneDeploy retries (`Ensure App Service is running` step)
  - Total: well over 60 min of CPU
- **Observed failure mode:** `state: QuotaExceeded`, `usageState: Exceeded`. `az webapp start` was throttled by the same quota — it returned `QuotaExceeded` 12 times in 60s. The Kudu deployment endpoint (`*.scm.azurewebsites.net/api/deployments/`) returned `403 Site Disabled`. CI deploys started failing at the `Ensure App Service is running` step instead of at the actual deploy step.
- **Why this fix is sustainable:** B1 costs ~$13/month but removes the daily CPU cap AND enables `alwaysOn: true` (so no more 60-second cold starts). Cold-start cost reduction alone is worth the spend on a dev-day cadence.
- **Why not "just wait for the quota to reset":** F1 quota resets at midnight UTC; current reset was in 11h 47m. Multi-day cadence + the cold-start tax on F1 means the next dev day would burn through it again. The cost of $13/mo is lower than the cost of an extra hour of blocked dev per day.
- **Cleanup:** The legacy `asp-poredoimage-f1` plan remains in the RG (empty, no cost). Delete it manually via `az appservice plan delete -g PoRedoImage -n asp-poredoimage-f1` when convenient. The `infra/main.bicep` defaults to creating `asp-poredoimage-b1` going forward; an `appServicePlanSkuName="F1"` override reverts to free tier (NOT recommended).
- **Revisit when:** Subscription credit / monthly budget becomes a real constraint (move back to F1 + reduce dev-day deploy cadence) OR a different App Service plan tier becomes available with Always On and no daily cap at the same price.

## ADR-026: Health Checks Must Detect Unresolved App Service Key Vault References, 2026-06-29

- **Decision:** The OpenAI and Computer Vision health checks (`Features/ImageAnalysis/OpenAIHealthCheck.cs`, `Features/ImageAnalysis/ComputerVisionHealthCheck.cs`) now explicitly detect the App Service Key Vault reference sentinel string (`@Microsoft.KeyVault(...)`) and surface it as `Degraded` with a remediation hint, **before** attempting the URL probe. BulkPromptStorageHealthCheck already had this guard (it was added when the storage check first hit the same race).
- **Why:** Production deploy #53 failed its post-deploy `/health` smoke test with 503s, and the Kudu `containerStream.log` showed the actual cause was a race between the Azure cold-start probe and the platform's Key Vault reference resolution:
  - The app process starts and reads the env var.
  - The env var is bound as `@Microsoft.KeyVault(VaultName=kv-poshared;SecretName=PoRedoImage-OpenAI-Endpoint)` — a non-empty **literal** string.
  - The app's `IConfiguration["OpenAI:Endpoint"]` returns the literal sentinel.
  - The existing `string.IsNullOrWhiteSpace` check **passes** (the sentinel is not blank).
  - The `HttpClient.SendAsync` then throws `System.InvalidOperationException: An invalid request URI was provided. Either the request URI must be an absolute URI or BaseAddress must be set.` because the literal is not a valid URL.
  - The catch block returns `Unhealthy` — which the overall aggregator turns into 503.
  - The CI smoke test treats 503 as a deploy failure.
- **What "good" looks like:** The check should distinguish:
  - `Unhealthy` = the probe tried to use a real, well-formed URL and the network/auth failed (real problem).
  - `Degraded` = config is missing or hasn't resolved yet (platform propagation delay, not a deploy bug).
- **Mapping (per ADR-017):** the post-deploy smoke test accepts `Degraded` and rejects `Unhealthy`. So a 200 with `Degraded` checks means the deploy succeeded and the platform just needs a few more seconds to resolve the KV references — the next /health hit (15s later, after the platform has populated the env var) will return all-`Healthy`.
- **Revisit when:** App Service adds first-class support for the platform to signal "reference unresolved" separately from "reference resolved to empty" (currently both surface as the literal reference string vs an empty string). Until then, every config-reading health check needs the explicit `@Microsoft.KeyVault(` guard.
- **Files touched:**
  - `src/PoRedoImage.Web/Features/ImageAnalysis/OpenAIHealthCheck.cs` — added `IsUnresolvedKeyVaultReference` guard.
  - `src/PoRedoImage.Web/Features/ImageAnalysis/ComputerVisionHealthCheck.cs` — same.
  - `infra/main.bicep` — moved to B1 Basic plan (see ADR-025, separate quota fix).
  - `.github/workflows/deploy.yml` — updated `EXPECTED_PLAN` to `asp-poredoimage-b1`.

## ADR-027: Grant Key Vault RBAC Before the Code Deploy/Restart, 2026-06-29

- **Decision:** In `.github/workflows/deploy.yml`, the "Bind Key Vault Secrets User role" step is moved to **before** the `azure/webapps-deploy@v3` code deploy (immediately after the bicep infra step), and a "Wait for Key Vault RBAC propagation" step (75s) is added between the role grant and the deploy. The post-deploy health smoke test is hardened: 12 attempts, a mid-loop `az webapp restart` (at attempt 5) to force `@Microsoft.KeyVault(...)` re-resolution after more propagation time, and a 30s `az webapp log tail` diagnostic on final failure so the startup exception behind a 503 is visible in the CI run.
- **Why:** Production deploy #54 failed with a 503 "Application Error" crash loop (the Azure HTML page, not the `/health` JSON). ADR-026 fixed the *health-check* race (unresolved KV ref → `Unhealthy`) for the case where the app **boots**. Deploy #54 was a different failure: the app **never booted**. The App Service platform resolves `@Microsoft.KeyVault(...)` App Settings **at container start** using the app's managed identity; if the identity does not yet hold (or has not propagated) `Key Vault Secrets User` on `kv-poshared`, the platform cannot resolve the references and the container refuses to start — the "Application Error" page. The previous workflow granted the role **after** the deploy/restart, so the container's first start had no vault access and crash-looped; the smoke test then raced the RBAC data-plane propagation (1–5+ min) inside a ~3.5-min window and lost. On the old F1 plan the identity/RBAC was already warm from prior deploys, so the race was masked; the F1→B1 plan move (ADR-025) triggered a fresh cold start that exposed it.
- **Pro:** The platform finds the `@Microsoft.KeyVault(...)` references resolvable at the deploy-restart, so the app boots on the first try instead of crash-looping. The 75s wait covers the common RBAC propagation window; the mid-loop restart covers the long tail without lengthening the happy path. The log-stream diagnostic turns a future 503 from "Application Error (mystery)" into the actual startup exception printed in the CI log.
- **Con:** Adds ~75s to every deploy (the propagation wait) even on re-runs where the role assignment already exists and is propagated. This is accepted: the alternative (grant-after-deploy) trades 75s of CI time for a flaky crash-loop that costs far more operator time to diagnose. The wait is a single `sleep` rather than a data-plane poll because the runner authenticates as the CI service principal, not the app's managed identity, so it cannot directly verify the app's data-plane access.
- **Alternatives considered:**
  - Retry the in-app `AddAzureKeyVault` load with backoff (`HostBootstrapExtensions.AddPoRedoImageKeyVault`). Rejected: `ConfigurationManager.AddAzureKeyVault` builds the source eagerly, so a failed attempt can leave a broken source in the builder and subsequent retries accumulate sources; the platform-level KV-ref resolution (the actual #54 blocker) is not helped by an in-app retry anyway.
  - Make `AddPoRedoImageKeyVault` log-and-continue on load failure (boot with unresolved refs, rely on `@Microsoft.KeyVault(...)` App Settings + `StartupSecretValidator`). Rejected: contradicts the ADR-021 fail-fast policy the team deliberately adopted to surface missing `az login` / RBAC, and the container still cannot start when the platform cannot resolve the refs.
- **Revisit when:** Azure RBAC propagation becomes reliably sub-30s (shorten/remove the wait), or App Service supports referencing Key Vault secrets without a pre-granted data-plane role at container start, or the CI service principal gains subscription-scope deployment permissions so the role assignment can move into the bicep module (ADR-023).
- **Files touched:**
  - `.github/workflows/deploy.yml` — reordered the role-assignment step before the deploy; added the RBAC propagation wait; hardened the health smoke test (12 attempts, mid-loop restart, log-stream diagnostic).

## ADR-028: PoShared Hub, Managed Identity, Naming Convention — Verified, 2026-06-29

- **Decision:** Lock in the existing Po* convention and document the cross-cutting guarantee: every Azure resource is anchored to the `PoShared` resource group (or the `PoRedoImage` RG for app-specific resources), authenticated via a **system-assigned managed identity** that holds **`Key Vault Secrets User`** on `kv-poshared` (RBAC ordering per ADR-027), and the AI runtime points at the **shared `po-aiservices-shared` OpenAI deployment** so dev/test/prod all read the same model version (`gpt-5.4-nano`). No connection strings for secrets anywhere in the project surface.
- **Status (2026-06-29):** Verified end-to-end:
  - `infra/main.bicep` provisions the App Service with `identity: { type: 'SystemAssigned' }` and binds the `@Microsoft.KeyVault(...)` App Settings via the `kvRef()` helper. Storage uses its own **system-assigned** MI; the `IUserImageRepository` + `IBulkPromptRepository` resolve endpoints from `IConfiguration` (Key Vault-sourced), not from raw connection strings. The `StartupSecretValidator` fail-fasts Production on any missing key.
  - **No connection strings for secrets anywhere** in the project surface: storage endpoints come from `IConfiguration["Storage:ConnectionString"]` which the `KeyVaultSecretNameMapping` populates from the `PoRedoImage-StorageConnectionString` Key Vault secret. Local dev is the only path that uses a literal `UseDevelopmentStorage=true` value, and that value is pinned in Dev only (see `HostBootstrapExtensions.AddPoRedoImageKeyVault`).
  - **Model version parity:** the `OpenAI:ChatCompletionsDeployment` value is a **literal** app setting in `infra/main.bicep` (NOT a Key Vault reference — KV reference caching previously returned a stale `gpt-4.1-nano` and produced 404 `DeploymentNotFound` at first call, per ADR-003). The literal is `gpt-5.4-nano`, matching the live deployment on `po-aiservices-shared`. Dev / Test / Prod all read this same value; there is no per-environment override.
  - **Naming convention** is the lowercase `po` prefix (per ADR-018), not strict PascalCase `Po{SolutionName}`. Storage / Web / Key Vault names are DNS-safe lowercase (`poredoimage-web`, `stporedoimage26`, `kv-poshared`); resource group names keep PascalCase (`PoRedoImage`, `PoShared`) since RGs allow it. The governance query in `SCRIPTS/audit-arg.ps1` enforces `^po` across the owned RGs.
- **What this ADR does NOT add:** No new code, no new resources. The convention is already enforced by ADRs 003, 005, 018, 023, 027 and the existing bicep + scripts. This ADR is the single point of reference that ties them together so a future agent doesn't accidentally break the guarantee.
- **Pro:** Single source of truth for naming, auth, and model version across all environments. A new environment (e.g. a `PoRedoImage-Staging` slot or a second RG) inherits the same guarantees by mirroring the bicep and the app-setting list.
- **Con:** The literal `gpt-5.4-nano` app setting in bicep must be updated in lockstep with the `po-aiservices-shared` deployment. A drift between bicep and the live deployment would surface as 404 at first call — but the post-deploy health smoke (per ADR-017 + the hardening in this session) catches the 404 as `Degraded` and surfaces the URL in the failure body. The remediation is a one-line bicep PR.
- **Revisit when:** (a) the model version needs to vary per environment (use a Bicep parameter keyed on `environmentName` rather than a hard-coded literal), OR (b) a non-shared AI resource is provisioned (the App Service would need a separate KV ref pointing at the new resource, which the current shared model would not auto-handle).
- **Files cross-referenced:** `infra/main.bicep`, `src/PoRedoImage.Web/Configuration/HostBootstrapExtensions.cs`, `src/PoRedoImage.Web/Configuration/KeyVaultSecretNameMapping.cs`, `SCRIPTS/audit-arg.ps1`, ADRs 003, 005, 018, 023, 027.

## ADR-029: Telemetry + Storage Lifecycle — Verified, 2026-06-29

- **Decision:** Lock in the existing production-profiling posture and the storage lifecycle so the budget is auditable: production uses **explicit Application Insights adaptive sampling at 0.1** (set in bicep, not in code), the **`ErrorPreservingSampler` keeps all error spans**, Serilog exports exceptions at **100%** through a separate sink, and metrics / heartbeat flow through the OTel metrics pipeline (not subject to the trace sampler). The storage account carries a **30-day delete + 7-day cool** lifecycle policy on block blobs, with a 7-day soft-delete window.
- **Status (2026-06-29):** Verified end-to-end. No code changes — this ADR records the existing posture as a single point of reference (item #9 of the "Top 10" sweep, 2026-06-29).
  - `ApplicationInsights__SamplingRatio=0.1` is set **explicitly** in `infra/main.bicep` so the portal shows the same ratio the app enforces. The code default (`Configuration/HostBootstrapExtensions.cs`) is the same value, so an App Setting accidentally dropped falls back to the code default instead of OpenTelemetry's 100% default.
  - `ErrorPreservingSampler` is registered **after** `UseAzureMonitor(...)` via `ConfigureOpenTelemetryTracerProvider`, so its `SetSampler` wins (per docs: last-registered sampler is used). It keeps spans whose `Status` is `Error` or whose HTTP status is ≥ 500; routine noise is dropped at the same ratio.
  - **Heartbeat is unaffected** by the trace sampler because the SDK heartbeat metric flows through the OTel metrics pipeline (`AddRuntimeInstrumentation` + `AddAspNetCoreInstrumentation`), not the tracer. A second signal: Serilog writes to Console / rolling file / App Insights via `TelemetryConverter.Traces`, which is a separate path from the trace sampler.
  - **Storage lifecycle** in `infra/main.bicep`: the `expire-generated-blobs-30d` rule tiers block blobs to **Cool after 7 days** and **deletes them after 30 days**. The blob service has a **7-day soft-delete** window. Regenerated user images and Kudu/app log blobs are covered.
- **What this ADR does NOT add:** No code, no bicep, no script changes. The decision is "the existing posture is correct; do not regress it."
- **Pro:** A future engineer can see the full budget in one place. If a PR touches the sampling ratio, the lifecycle rule, or the ErrorPreservingSampler, this ADR is the "before" snapshot that the PR diff should be measured against.
- **Con:** The 0.1 trace sampling is intentionally lossy; if a production incident requires full-fidelity trace, the resolution is to set `ApplicationInsights__SamplingRatio=1.0` via the portal / app setting for the duration of the investigation and reset it afterwards. Documented in the `ErrorPreservingSampler` docstring.
- **Revisit when:** (a) trace storage costs become a complaint (lower the ratio to 0.05; ErrorPreservingSampler keeps the error signal regardless), OR (b) the lifecycle policy needs per-container tuning (move the rule to `infra/main.bicep` parameters keyed on container name).
- **Files cross-referenced:** `infra/main.bicep` (`expire-generated-blobs-30d` lifecycle + `ApplicationInsights__SamplingRatio`), `src/PoRedoImage.Web/Configuration/HostBootstrapExtensions.cs` (`AddPoRedoImageTelemetry` + `ErrorPreservingSampler`), ADRs 015, 020, 025.

## ADR-030: ARG Governance Adds Optional Auto-Stop (Read-Only Default), 2026-06-29

- **Decision:** `SCRIPTS/audit-arg.ps1` remains **read-only by default** (its existing posture), with a new `-AutoStopIdleCompute` switch that — when the operator passes it on a one-off run — `az webapp stop`s any App Service Plan averaging **< 5% CPU over 7 days** in the owned RGs. **No deletion**, no automatic shutdown on a schedule, no orphan-pruning, no naming-violation auto-cleanup. The auto-stop target list is always printed first so the operator can abort with Ctrl-C before the stop call lands.
- **Why:** The "Top 10" sweep (item #8, 2026-06-29) asked the audit script to "immediately flag and shut down orphan assets, idle compute, and naming-violating resources." Reviewing the current script and the existing ADR-018 safety design, the team has consistently treated deletion as a **human decision** (F1 plans legitimately look idle; a "stray" resource may be shared infra). Auto-**stop** (not delete) is a meaningful compromise:
  - **Stop** is reversible (`az webapp start` brings it back in seconds) and has a narrow blast radius (the plan is stopped, no data loss, no RBAC impact).
  - **Stop** saves money on B1+ idle plans (a stopped B1 still incurs the plan reservation but no compute, ~half cost; the B1 plan in this project is dev-only so the savings are modest).
  - **Delete** is irreversible, and a "stray" resource can be a `Microsoft.Network/virtualNetworks` that another RG depends on — auto-deletion would have cascading consequences.
  - **Naming-violation auto-cleanup** is the most dangerous of the three (false positives in the regex are common — e.g. an `admin` storage container), so it stays manual.
- **Safety gates before the auto-stop fires:**
  1. The switch is opt-in; the default behavior is the existing report-only mode.
  2. The list of plans that would be stopped is printed and the script **sleeps 10s** with a clear "press Ctrl-C to abort" message before the first `az webapp stop` call.
  3. The F1 plan (`asp-poredoimage-f1`, now empty) is **excluded** by name — the F1 was always expected to look idle, and stopping an empty F1 is a no-op anyway but the exclusion makes the intent auditable.
  4. The B1 plan (`asp-poredoimage-b1`) is **included** — it is the prod plan; a 5%-CPU/7d average over a holiday weekend is a real signal of underuse worth surfacing to the operator.
  5. A `-DryRun` switch is accepted for forward-compat (mirrors the `cleanup-testcontainers.ps1` pattern from `userMemory/testcontainers-cleanup.md`); the current implementation is already a dry run unless `-AutoStopIdleCompute` is passed.
- **What this ADR does NOT add:** No scheduled auto-shutdown. The script is still operator-invoked; no cron / GitHub Action calls it. If a future change wants scheduled auto-shutdown, it goes in a separate ADR with a kill-switch and an opt-out per-resource.
- **Pro:** Meets the "Top 10" intent (flag-and-act) for the most recoverable action (stop) while preserving the existing safety design for the irreversible ones (delete, naming-prune).
- **Con:** Adding a switch to a previously read-only script is a footgun in itself — a future operator running the script with the default flags gets the existing report, and only an operator who reads the help and passes the new switch gets the new behavior. Documented in the script's `Get-Help` block.
- **Revisit when:** (a) a real recurring-idle problem appears (move auto-stop to a scheduled workflow with an explicit kill-switch and per-resource allowlist), OR (b) the team decides the cost savings from auto-deletion outweigh the irreversibility risk (separate ADR; this one would be superseded).
- **Files touched:**
  - `SCRIPTS/audit-arg.ps1` — added `[switch]$AutoStopIdleCompute` parameter, 10s pre-action sleep, F1 plan exclusion, dry-run path.
