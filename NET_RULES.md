# NET_RULES

Zero-waste engineering rules for `Po*` .NET solutions.

> **Amended 2026-07-27** following a full compliance audit of PoRedoImage. Rules that described a
> structure the codebase had deliberately diverged from now describe the decision that was actually
> made, with the reasoning recorded inline. Amended clauses are marked **[A]**. Everything unmarked
> is unchanged from the original ruleset.

---

## 1. Core Principles & Governance

- **Naming Convention:** All solutions, projects, and root namespaces use the `Po{Name}` prefix
  (e.g. `PoWatch`, `PoWalker`).
- **Target Stack:** .NET 10. **[A]** Language is **C# 14** — the version .NET 10 ships. (The original
  ruleset said C# 15, which does not exist for this SDK; projects set
  `<LangVersion>latest</LangVersion>` and get C# 14.) Dependencies are managed centrally via
  `/Directory.Packages.props`.
- **Compiler Directives:** Enforce zero warnings and strict null safety across all projects:

  ```xml
  <PropertyGroup>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  ```

- **Git Strategy:** Trunk-based development directly on `master`. No feature branches unless requested.
- **Domain Integrity:** Eradicate primitive obsession. Enforce strongly-typed IDs
  (`readonly record struct`) and enums. Zero magic strings.
  - **[A]** Configuration keys count as magic strings. Every key must be a named constant in a single
    `ConfigKeys` class in the `Shared` project (reachable from both Web-SDK and plain-SDK projects).
    `IOptions<T>` remains preferred for sections needing validation or hot-reload; direct
    `IConfiguration[ConfigKeys.X]` reads are permitted where a singleton must re-read a rotated Key
    Vault secret on each call.

## 2. Directory & Architecture Layout

**[A]** The original ruleset prescribed exactly three `src/` projects (`API`, `Client`, `Shared`).
PoRedoImage ships six. The divergence is deliberate — Vertical Slice Architecture governs features
while a thin Onion core supplies cross-slice primitives — and the ruleset now records that decision
rather than contradicting it:

```
/
├── AGENT.MD                      # single living architectural source of truth
├── NET_RULES.md
├── Directory.Packages.props
├── SCRIPTS/
│   └── setup.ps1
├── src/
│   ├── Po{Name}.Domain/          # entities, interfaces — cross-slice primitives
│   ├── Po{Name}.Application/     # orchestration, agents
│   ├── Po{Name}.Infrastructure/  # repositories, external services, mocks
│   ├── Po{Name}.Shared/          # DTOs, enums, validation, ConfigKeys
│   ├── Po{Name}.Client/          # Blazor WASM UI application
│   └── Po{Name}.Web/             # Minimal API, BFF host, VSA feature slices
└── tests/
    ├── Po{Name}.Tests.Unit/           # pure logic, no I/O
    ├── Po{Name}.Tests.Integration/    # Azurite / Testcontainers
    ├── Po{Name}.Tests.E2E.ApiSmoke/   # pure HTTP API contract testing
    └── Po{Name}.Tests.E2E.UI/         # Playwright UI testing (mobile/desktop)
```

- **[A]** The API/BFF host is named `Po{Name}.Web`, not `.API` — it serves the WASM client as well as
  the API, and `.Web` names that dual role honestly.
- **[A]** Test projects use the `Po{Name}.Tests.*` prefix so they sort together and read
  unambiguously as tests.
- **[A] Directory depth:** maximum **3** levels within `src/`, and only when the third level is a
  feature, slice, or capability folder (`Features/ImageAnalysis/`, `Agents/StyleDirector/`,
  `Pages/BulkGenerate/`). Vendored asset trees under `wwwroot/` are exempt. A third level that merely
  groups by type rather than by feature is still a violation.

### Vertical Slice Rules

- **Flattened Features:** Minimal API endpoints, request/response DTOs, and handlers live together in
  `Po{Name}.Web/Features/{FeatureName}`.
- **Autonomous Slices:** Slices must not reference each other. Shared models belong in
  `Po{Name}.Shared`; cross-slice server vocabulary (policy names, filters) belongs in
  `Po{Name}.Web/Features/Shared`.
  - **[A]** A slice requiring an authorization policy references the name from
    `Features/Shared/AuthorizationPolicies`, never from the Auth slice directly.
- **VSA wins over Onion** whenever both would apply. A new feature becomes a new slice, never a new
  Onion layer.
- **Client Hosting:** The Web project directly hosts and serves the Blazor WASM client.

## 3. API, Security & BFF Pattern

- **Endpoints:** Map via `IEndpointRouteBuilder` + `MapGroup()`. Document with
  `Microsoft.AspNetCore.OpenApi` and Scalar UI.
- **Diagnostics:** Expose `/health` and **[A]** `/api/diag` (the API-prefixed path keeps it inside the
  same auth and rate-limiting pipeline as every other API route). `/api/diag` must strictly mask
  secret values.
- **BFF Architecture:**
  - **Zero Tokens in Browser:** Blazor WASM interacts solely via `HttpOnly`, `SameSite=Strict` secure
    cookies.
  - **OAuth:** Entra ID uses the `/common` endpoint with a server-side `FallbackPolicy`.
  - **Propagation:** Pass `X-Session-ID` and `X-Correlation-ID` through all HTTP calls — **[A]** both
    legs: browser → BFF (request middleware) *and* BFF → downstream services (an outbound
    `DelegatingHandler` on every named client). A chain that stops at the server boundary does not
    satisfy this rule.
- **Dev/Test Auth:** Use `FakeAuthHandler` driven by `X-Fake-User` and `X-Fake-Roles` headers.
  - ⚠️ **Guardrail:** `FakeAuthHandler` MUST throw `InvalidOperationException` if initialized in a
    Production environment.

## 4. UI/UX & Blazor WASM Standards

- **Layout Contract:** Header layout — Left (Branding) | Center (Actions) | Right (Session / Logout).
- **State Visibility:** If local mock data is active, display a persistent "USING MOCK DATA" banner.
- **Styles & Themes:**
  - Inline styles forbidden. Use scoped CSS (`.razor.css`) and `:root` CSS custom properties for
    design tokens.
  - **[A] Sole exemption:** a `style` attribute that only assigns a CSS **custom property** from
    computed state (`style="--progress: @value"`) is permitted. It is the supported mechanism for
    passing dynamic values into scoped CSS; the styling itself must still live in the stylesheet.
    Assigning any real CSS property inline remains a violation.
  - Styles for elements rendered by a child component must live in the global sheet — scoped CSS does
    not reach across the component boundary.
  - Support system-aware Light/Dark themes dynamically.
- **Performance:** Use `Virtualize` for long lists. WebGL/Canvas for complex visual acceleration.
- **Accessibility:** WCAG 2.2 Level AA compliance on all interactive elements.
  - **[A]** Compliance must be **verified**, not asserted: the E2E UI suite runs axe-core against the
    anonymous entry page and the primary authenticated page and fails on any violation tagged
    `wcag2a`, `wcag2aa`, `wcag21aa`, or `wcag22aa`. Hand-written `aria-*` attributes with nothing
    checking them do not satisfy this rule.

## 5. Local AI, Observability & Performance

- **Local AI Execution:** Implement model registries with dtype fallback chains for browser/
  worker-native execution.
  - **[A]** The registry, the capability-based chain pruning, and the advance-on-failure logic live in
    one C# code path; JS runtime adapters only *interpret* a variant. Where more than one runtime is
    used, they must not imply more than one policy. See
    `docs/superpowers/specs/2026-07-27-local-ai-browser-execution-design.md`.
- **AI Test Interception:** Intercept AI pipeline calls via a custom `DelegatingHandler` in test
  environments to prevent token consumption.
- **Zero-Allocation Logging:** Use `[LoggerMessage]` source generators on high-frequency paths. Avoid
  string interpolation in logs.
  - **[A]** "High-frequency" means per-request and per-item paths — outbound AI calls, request
    pipelines, loops. Startup, configuration-validation, and exception-handler logging is explicitly
    out of scope: it runs once or rarely and gains nothing from source generation.
- **Resilience & Cache:** Standardize HTTP resilience and caching via .NET 10
  `AddStandardResilienceHandler` and `HybridCache`.

## 6. Testing, CI/CD & Hygiene

- **[A] Number of tests — per-tier ceilings, not targets:** 100 Unit | 50 Integration | 25 API E2E |
  25 UI E2E. These are upper bounds enforced by `TestCountCeilingTests` in each tier. The intent is to
  prevent test-suite sprawl and keep each tier meaningful, not to mandate a headcount; a tier under
  its ceiling is compliant.
- **Azure Infrastructure:**
  - Provision resources under resource group `PoShared` (or `Po{SolutionName}`).
  - Authenticate using System-Assigned Managed Identity + Key Vault. No raw connection strings in app
    settings.
- **[A] CI:** A pull-request workflow builds the solution and runs the **Unit and Integration** tiers.
  The E2E tiers are excluded by design — both target a running instance and self-skip when none is
  reachable, so running them there would report green without testing anything.
- **Post-Deploy Smoke Test:** The deploy pipeline must execute post-deploy checks against:
  - Blazor render tree initialization — **[A]** concretely: the host document emits a webassembly
    component marker, the boot script is served, and the client assembly appears in the boot manifest
    (a publish-trimming regression can strip every component while the host page still returns 200).
  - The `/health` endpoint.
  - Safe retrieval of masked configuration from `/api/diag` — **[A]** where the endpoint is auth-gated
    in Production and the pipeline is anonymous, prove the inverse: the endpoint gates correctly and
    no anonymous response body contains unmasked secret material. `MaskValue`'s contract is covered by
    the unit and integration tiers.
- **Workspace Hygiene:** Continuously purge dead code and orphaned assets. Maintain **one** `AGENT.MD`
  as the living architectural source of truth — **[A]** a second copy under `docs/` is a violation;
  it will drift.
