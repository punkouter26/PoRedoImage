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

## ADR-001: Blazor Web App (SSR + Interactive Server + WASM)

- **Decision:** Use .NET 10 Blazor Web App with hybrid SSR + Interactive Server + WASM rendering.
- **Why:** Single language (C#) across full stack. SSR for fast initial load, WASM for rich interactivity, SignalR for real-time sync.
- **Alternatives:** React SPA + .NET API (rejected: two languages, two build systems).
- **Trade-off:** Larger initial bundle; mitigated by SSR pre-rendering.

## ADR-002: Minimal APIs (No MVC Controllers)

- **Decision:** All endpoints use `MapGroup` + static handler methods. No `Controller` classes.
- **Why:** Vertical Slice Architecture — each feature owns its endpoint + logic co-located. Reduces ceremony, improves token efficiency.
- **Alternatives:** MVC controllers with `[ApiController]` (rejected: ceremony, routing overhead).
- **Trade-off:** Less "standard" for .NET devs familiar with MVC; mitigated by consistent pattern.

## ADR-003: Azure Computer Vision + OpenAI + Gemini (Multi-AI)

- **Decision:** Chain Azure CV (vision) → Azure OpenAI GPT-4.1-nano (language) → Google Gemini Imagen3 (generation).
- **Why:** Each AI service excels at its domain. CV for tagging, OpenAI for description enhancement, Gemini for image generation.
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
## ADR-013: Two-Tier Test Layout (Unit/Integration + E2E)

- **Decision:** Consolidate the four initial test projects (Tests.Unit, Tests.Integration, Tests.E2EAPI, Tests.E2EUI) into three: Tests.Unit, Tests.Integration, Tests.E2E. The latter merges HTTP smoke + C# Playwright browser tests under one LiveServerFactAttribute.
- **Why:** Single base-URL resolver, single attribute, single fixture graph. The duplicate LiveServerFactAttribute (byte-for-byte the same in both E2E projects) was a known smell.
- **Status (2026-06):** E2EAPI + E2EUI merged into Tests.E2E. The remaining Tests.Unit + Tests.Integration split mirrors test-runner conventions (Testcontainers-backed tests vs. pure logic tests) and is intentional — combining them would force unit tests to take an IClassFixture<WebApplicationFactory> even when they don't need one.
- **Trade-off:** Two remaining test projects instead of the audit-recommended one. Justified by the run-time cost difference (Azurite container vs. in-process).

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
