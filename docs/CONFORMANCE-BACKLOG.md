# PoRedoImage — NET_RULES Conformance Backlog

Derived from the full §1–§7 conformance audit (2026-07-11). Items are ranked by priority.
Effort key: **S** ≤ ½ day · **M** ~1–2 days · **L** > 2 days / needs product decision.

Legend: ✅ done this pass · ⬜ open · 🔷 needs a decision (spec vs. deliberate engineering choice).

---

## Done this pass
- ✅ **AGENT.MD drift** — test layout described the 4-project split as "future / 3 merged"; it is complete. Fixed, plus documented the new fail-closed authz posture and correlation headers (§6.8).
- ✅ **§4.2 cookie hardening** — `HttpOnly` + `SameSite=Strict` + `Secure` (Always outside Dev) on both cookie schemes (`AuthServiceExtensions.HardenCookie`).
- ✅ **§4.5 server-side authz** — `AuthorizationOptions.FallbackPolicy = RequireAuthenticatedUser`, with `.AllowAnonymous()` on the public surface (WASM host, static assets, `/health`, `/alive`, favicon, OpenAPI/Scalar, `/auth/*`, `/api/diag/mock-status`). ⚠️ **Needs a login-flow smoke test** (dev-guest + full OIDC round-trip) before merge — compile-verified only; OIDC could not be driven in the audit environment.
- ✅ **§6.9 correlation** — WASM `CorrelationHeaderHandler` stamps `X-Session-ID` (per tab) + `X-Correlation-ID` (per request); server middleware reads/echoes/logs both.

---

## P0 — Critical / blocking

| # | Rule | Item | Effort |
|---|---|---|---|
| P0-1 | build | **`Microsoft.OpenApi 2.0.0` high-severity advisory (GHSA-v5pm-xwqc-g5wc)** breaks `dotnet restore` under `TreatWarningsAsErrors` (NU1903). Bump to the patched version in `Directory.Packages.props`. Currently the whole solution cannot restore/build cleanly. | S |
| P0-2 | §7 | **Local-first Web Worker VLM is entirely absent** — no transformers.js/onnxruntime-web, no worker, no model registry, no dtype fallback, no pinned/offline model supply chain. Inference is fully server-side. **Product decision first:** is §7 in scope, or should the spec drop it? If in scope, this is a multi-day feature (worker + model-class registry + fp16→q8→q4 fallback + pinned local assets). | 🔷 L |

---

## P1 — High

| # | Rule | Item | Effort |
|---|---|---|---|
| P1-1 | §5.4 | **Prod Table/Blob Storage uses a secret connection string, not the Managed Identity** that is already provisioned + granted `Key Vault Secrets User`. Switch repos to `TableServiceClient(uri, new DefaultAzureCredential())` in cloud envs (keep Azurite conn-string path for local). | M |
| P1-2 | §6.6 | 🔷 **`IsTrimmable` conflict.** Spec mandates `IsTrimmable=true` on `.Client`/`.Shared`; commit `30f3070` removed it because trimming stripped render-mode-resolved Routes/pages + STJ DTO ctors. **Decision:** amend the spec for this render model, OR reintroduce trimming with explicit `[DynamicDependency]`/`TrimmerRootDescriptor` roots. | 🔷 M |
| P1-3 | §2.1 | 🔷 **Clean-Architecture layers vs. flat VSA slices.** `Web/Features/*` delegate into separate `.Domain`/`.Application`/`.Infrastructure` projects; spec §2.1 wants logic co-located per slice. Large refactor — confirm intent before moving. AGENT.MD already documents "VSA wins over Onion"; may just be a spec-vs-reality reconciliation. | 🔷 L |

---

## P2 — Medium

| # | Rule | Item | Effort |
|---|---|---|---|
| P2-1 | §5.5 | Writes are unconditional `UpsertEntityAsync` (last-writer-wins). Add **ETag/`IfMatch` optimistic concurrency**, rewrite-then-delete, and treat **HTTP 409 as success**. | M |
| P2-2 | §4.6 | No explicit `SlidingExpiration`/`ExpireTimeSpan` or **kiosk-durable session** tuning; no graceful-reconnect handling. Add to the cookie options. | S |
| P2-3 | §6.3 | Prod uses **fixed 10 % ratio sampling**, not adaptive @ 10 items/sec; **no Live Metrics**; a `Test` env would wrongly fall to 0.1. Align sampler + enable Live Metrics. | M |
| P2-4 | §6.2 | `cloud_RoleName` is set via OTel `ConfigureResource` (host-only ✓) but there is **no `ITelemetryInitializer`** as §6.2 literally specifies. Add one, or amend spec to accept the OTel-resource approach. | S |
| P2-5 | §5.2 | 🔷 App Service Plan is **B1, not free F1** (deliberate — ADR-025, F1's 60-min/day CPU cap). Reconcile spec §5.2 with the ADR. | 🔷 S |
| P2-6 | §1.5 | No **strongly-typed IDs** (`Id` is raw `string`); `MemeTemplate.Alignment/Category` are free strings; `AuthEndpoints` uses `IsEnvironment("Test")` literal instead of `PoEnvironments.Test`. Introduce typed IDs/enums. | M |
| P2-7 | §2.2 | `.Shared` leaks a `ProjectReference` to `.Domain` + non-DTO helpers; **zero FluentValidation validators exist** (validation is a `ValidationFilter`). Reconcile: adopt FluentValidation, or amend spec. | M |
| P2-8 | §5.6 | Fail-fast startup validates config **presence**, not dependency **reachability**. Add a boot-time connectivity gate (Storage/OpenAI/Key Vault) if strict fail-fast is required. | M |
| P2-9 | §3.2 | Diag route is `/api/diag` (spec wants `/diag`) and is admin-auth-gated rather than an open ping. Add a `/diag` alias or reconcile spec. | S |
| P2-10 | §3.4 | `setup.ps1` references none of the mandated ecosystem tools (`gstack`, `Understand-Anything`, `graphify`, `absolute`). Add or drop from spec. | S |
| P2-11 | §5.1 | Key Vault secret names use single-dash (`PoRedoImage-AzureAd-ClientSecret`); spec wants double-dash (`--`). Rename in bicep + KV. | S |

---

## P3 — Low / cosmetic

| # | Rule | Item |
|---|---|---|
| P3-1 | §3.3 | Endpoint extensions take `this WebApplication`, not `IEndpointRouteBuilder`. |
| P3-2 | §2.2 | Test project names differ from spec labels (`.Tests.Unit` vs `.UnitTests`, etc.) — count is correct. |
| P3-3 | §1.4 | No `appsettings.Test.json`/launch profile; Test env wired only via fixture code. |
| P3-4 | §4.3 | `ValidateIssuer` relies on default `true`; with no tenant allow-list, any well-formed MS tenant is accepted (by design). |
| P3-5 | §4.4 | Guest button renders in Dev only, not Test (Test bypass is endpoint-layer). |
| P3-6 | §5.3 | Azurite container is `poredoimage-azurite-dev`, not exactly the solution name. |
| P3-7 | tests | Stray loose `tests/TestCounting.cs` sits outside any project. |

---

## Suggested sequencing
1. **P0-1** (unblocks the build — do immediately).
2. Smoke-test the FallbackPolicy/OIDC change shipped this pass.
3. **P0-2 / P1-2 / P1-3 / P2-5 / P2-7** decisions (spec-vs-reality) — batch into one review; several may resolve by amending NET_RULES rather than code.
4. **P1-1** (Managed Identity) — highest-value security hardening.
5. Remaining P2 by env impact, then P3 cleanup.
