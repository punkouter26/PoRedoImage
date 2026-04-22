# PoRedoImage — Copilot Instructions

> Detailed per-area rules live in `.github/instructions/`. The files below are loaded automatically — do **not** restate their content here; link to them instead.
>
> | Area | File |
> |------|------|
> | Web host (Blazor SSR + Minimal APIs) | [poredoimage-web.instructions.md](.github/instructions/poredoimage-web.instructions.md) |
> | Blazor WASM client | [poredoimage-client.instructions.md](.github/instructions/poredoimage-client.instructions.md) |
> | Domain entities & interfaces | [poredoimage-domain.instructions.md](.github/instructions/poredoimage-domain.instructions.md) |
> | Application orchestration | [poredoimage-application.instructions.md](.github/instructions/poredoimage-application.instructions.md) |
> | Infrastructure (Azure SDKs, ImageSharp) | [poredoimage-infrastructure.instructions.md](.github/instructions/poredoimage-infrastructure.instructions.md) |
> | Shared DTOs | [poredoimage-shared.instructions.md](.github/instructions/poredoimage-shared.instructions.md) |
> | Unit tests | [poredoimage-tests-unit.instructions.md](.github/instructions/poredoimage-tests-unit.instructions.md) |
> | Integration tests | [poredoimage-tests-integration.instructions.md](.github/instructions/poredoimage-tests-integration.instructions.md) |

---

## What This Project Is

**PoRedoImage** is a .NET 10 Blazor Web App (SSR + Interactive Server) that chains Azure Computer Vision → Azure OpenAI GPT-4.1-nano → Google Gemini Imagen 3 to transform photos into AI-regenerated images, memes, and bulk art-style variations.

Live: <https://poredoimage-web.azurewebsites.net> · API docs: `/scalar/v1` · Health: `/health`

---

## Repository Layout

```
src/
  PoRedoImage.Web/          # ASP.NET Core host — Blazor SSR + Minimal API slices
  PoRedoImage.Client/       # Blazor WASM client (loaded as additional assembly)
  PoRedoImage.Application/  # Orchestration use cases
  PoRedoImage.Infrastructure/ # Azure SDK + ImageSharp implementations
  PoRedoImage.Domain/       # Entities + interface contracts (zero NuGet deps)
  PoRedoImage.Shared/       # DTOs shared between Web host and WASM client
tests/
  PoRedoImage.Tests.Unit/        # Pure logic, no I/O
  PoRedoImage.Tests.Integration/ # WebApplicationFactory + Testcontainers
  playwright/                    # E2E — TypeScript + Playwright Chromium
infra/
  main.bicep              # App Service + Storage Account provisioning
docs/
  *.mmd                   # Mermaid architecture, flow, and state diagrams
```

---

## Global Conventions (all projects)

- **Target framework**: `net10.0`; **SDK pin**: `10.0.100` (`global.json`, `rollForward: latestFeature`).
- **`Directory.Build.props`** applies to every project: `Nullable enable`, `ImplicitUsings enable`, `TreatWarningsAsErrors true`. Resolve all nullable warnings before committing.
- **Central package management** via `Directory.Packages.props` — never add `Version=` attributes to individual `<PackageReference>` items. All versions are pinned there.
- **Namespace prefix**: `PoRedoImage.*` for every project and Azure resource name.
- **No MVC controllers** — all HTTP surface is Minimal APIs; use `MapGroup` + static handler methods.
- **Vertical Slice Architecture** — each Web feature lives entirely under `src/PoRedoImage.Web/Features/{FeatureName}/` (endpoints, health checks, middleware co-located).
- **`ValidationFilter<T>`** (at `Features/ValidationFilter.cs`) bridges DataAnnotations to Minimal APIs; attach it with `.AddEndpointFilter<ValidationFilter<T>>()` on any endpoint that accepts a request DTO.

---

## Dependency Direction

```
Web / Client  →  Application  →  Domain  ←  Infrastructure
                    ↑                              ↑
                 Shared DTOs ────────────────────────
```

- `Domain` has **zero** NuGet dependencies — keep it that way.
- `Infrastructure` is the only project that may reference Azure SDKs, ImageSharp, or external HTTP clients.
- `Application` may reference `Microsoft.Extensions.Logging.Abstractions` and `Microsoft.Extensions.Http` only.

---

## Feature Flags

`appsettings.json` exposes `FeatureFlags` for conditional feature activation:

| Flag | Default | Purpose |
|------|---------|---------|
| `UseRealAiInDev` | `true` | Use live AI services in Development |
| `UseRealAiInIntegrationTests` | `false` | Guard: keep AI mocked in CI |
| `EnableBulkGenerate` | `true` | Bulk art-style generation feature |
| `EnableMemeGeneration` | `true` | Meme overlay feature |
| `EnableImagen3` | `true` | Gemini Imagen 3 image generation |

Check `IConfiguration["FeatureFlags:X"]` when gating new AI-dependent features.

---

## Auth Flow (condensed)

- **Development, no `AzureAd:ClientId`** → cookie-only; use `/dev-login?email=X` bypass.
- **All other environments** → Microsoft Entra ID OIDC (`AddOpenIdConnect`) + cookie.
- `AzureAd:AllowedTenantIds` (comma-separated) restricts to specific tenants; absent → issuer validation disabled (multi-tenant/personal accounts allowed).
- User ID claim: `ClaimTypes.NameIdentifier`. Always extract from `HttpContext.User` — never trust a body field.

---

## Rate Limiting

AI endpoints use the `"ai-endpoints"` sliding-window policy (10 req/min per user ID, fallback to IP). Apply with `.RequireRateLimiting("ai-endpoints")`. HTTP 429 is returned on breach.

---

## Observability

- **Serilog** structured logging; message templates only — no string concatenation.
- Enrich all logs with `CorrelationId` (set by `CorrelationIdMiddleware`) and `UserId`/`SessionId` (set by `UserContextMiddleware`).
- **OpenTelemetry** traces and metrics exported to Azure Monitor when `ApplicationInsights:ConnectionString` is set; instrumentation is always active (zero cost when not exporting).
- Health checks at `/health` (JSON, all named checks) and `/alive` (liveness probe, no checks).

---

## Key Vault & Secret Rotation

- Secrets loaded at startup from `AZURE_KEY_VAULT_ENDPOINT` via `KeyVaultSecretNameMapping` (prefix `PoRedoImage-` → colon-separated config key).
- `ReloadInterval = 30 min` — singletons must re-read credentials on every call via `AzureKeyCredential.Update(...)`.
- Dev: Key Vault failure → warning + continue. Non-dev: fatal + throw.

---

## Build & Test Commands

```bash
# Restore
dotnet restore PoRedoImage.slnx

# Build
dotnet build PoRedoImage.slnx --configuration Release

# Unit + Integration tests (with coverage)
dotnet test PoRedoImage.slnx --configuration Release --collect:"XPlat Code Coverage"

# Unit tests only
dotnet test tests/PoRedoImage.Tests.Unit

# Integration tests (skip Docker)
dotnet test tests/PoRedoImage.Tests.Integration --filter "Category!=Docker"

# E2E (requires .NET server to be running or auto-started by Playwright)
cd tests/playwright && npx playwright test

# Run Web host (dev)
dotnet run --project src/PoRedoImage.Web
# → http://localhost:5000 | dev login: /dev-login?email=you@example.com
```

---

## CI/CD

| Workflow | Trigger | What it does |
|----------|---------|-------------|
| `ci.yml` | PR → `master` | Restore → Build → Unit+Integration tests → 80% coverage gate (warn-only) |
| `azure-deploy.yml` | Push to `master` / manual | Publish → OIDC login → deploy to `poredoimage-web` App Service |
| `e2e.yml` | (separate) | Playwright Chromium against the live or local server |

- Deploy uses **OIDC** (`azure/login@v2`); secrets required: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` — no long-lived credentials in the repo.
- CI installs `fonts-liberation` for Linux ImageSharp text rendering; add any new system font dependencies to the deploy workflow's "Install fonts" step.

---

## Infrastructure (Bicep)

`infra/main.bicep` provisions:
- `stporedoimage26` — Azure Storage Account (Standard_LRS, TLS 1.2, no public blob access)
- `poredoimage-web` — Azure App Service on shared Linux plan `asp-poshared-linux` (PoShared RG) with system-assigned managed identity

Shared services (Key Vault `kv-poshared`, OpenAI, Application Insights) live in the `PoShared` resource group — do not re-provision them here.

Deploy:
```bash
az deployment group create -g PoRedoImage -f infra/main.bicep
```