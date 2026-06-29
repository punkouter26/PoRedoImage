# PoRedoImage — AI Agent Context

## Overview
PoRedoImage is a Blazor WebAssembly (global Interactive WebAssembly, no prerender) SPA for AI-powered
image analysis, regeneration, meme generation, and bulk generation, hosted behind an ASP.NET Core
Backend-for-Frontend (BFF). The `.Web` project is the API/BFF host only; all interactive UI (router,
layout, pages) lives in and runs from the `.Client` WASM assembly. The server side organises by
**Vertical Slice Architecture (VSA)** — feature folders under `.Web/Features/{Auth, BulkGenerate, ...}`
co-locate endpoint, DTO, and validation per slice — and the Domain / Application / Infrastructure
projects supply cross-cutting primitives (entities, interfaces, repository implementations) consumed
by the slices. **VSA wins over Onion when both would apply** to a feature; Onion layering is
preserved only for cross-slice concerns.

## Quick Facts
- **Solution**: PoRedoImage (.slnx format)
- **Master Prefix**: `PoRedoImage` for namespaces, Azure resources
- **Framework**: .NET 10 (pinned via global.json)
- **Client**: Blazor WASM (global InteractiveWebAssembly, prerender disabled) + Radzen UI — owns router, layout, all pages
- **Server (.Web)**: API/BFF host — minimal-API endpoints, OIDC+cookie auth, serves the WASM client
- **Ports**: HTTP 5000 / HTTPS 5001 (fixed)
- **Database**: Azure Table Storage (Azurite locally)
- **Auth**: Microsoft OIDC (prod) + GUEST mode (dev/test)
- **Telemetry**: OpenTelemetry → Application Insights + Serilog
- **Versioning**: MinVer via Git tags

## Project Structure
```
src/
├── PoRedoImage.Domain/       # Entities, Interfaces (cross-slice primitives)
├── PoRedoImage.Application/  # Service orchestration
├── PoRedoImage.Infrastructure/ # Repositories, external services, IMockable mocks
├── PoRedoImage.Shared/       # DTOs + FluentValidation shared between WASM and API
├── PoRedoImage.Client/       # Blazor WASM SPA — Routes, Layout, Pages, Shared, Models, wwwroot
└── PoRedoImage.Web/          # API/BFF host (App.razor host doc + Features/*/ endpoints)
tests/                          # Target = 4 projects per spec; current state has 3 (E2EAPI + E2EUI
                                # are merged in Tests.E2E). The 4-project split is tracked separately
                                # (see ADR-022 / "Idea #1" in the audit output).
├── PoRedoImage.Tests.Unit/        # xUnit + Moq — pure logic
├── PoRedoImage.Tests.Integration/ # xUnit + Testcontainers (Azurite) + WebApplicationFactory
├── PoRedoImage.Tests.E2E/         # CURRENT: HTTP API smoke + C# Playwright UI under [LiveServerFact]
└── … future split →
    ├── PoRedoImage.Tests.E2E.ApiSmoke/  # pure HTTP API smoke (no Playwright dependency)
    └── PoRedoImage.Tests.E2E.UI/        # C# Playwright browser tests (Playwright + Chromium install)
```
> Render model: only `.Client` ships to the browser, so every interactive component lives there.
> `.Web` holds just `Components/App.razor` (host document) + `_Imports.razor`; it renders the Client's
> `<Routes>`/`<HeadOutlet>` with `RenderModes.WasmNoPrerender`. Server-side `Features/{Name}/` folders
> own their endpoints, DTOs, validators, and feature-specific services.

## Key Architecture Rules
1. **VSA on the server + WASM/BFF on the client**: `.Web/Features/{Name}/` slices own their endpoints, DTOs, and validators. Domain/Application/Infrastructure supply cross-slice primitives. **VSA wins over Onion when both would apply** — a new feature goes into a new slice, not a new layer in Onion.
2. **No AOT**: Blazor WASM uses interpreted mode for smaller builds and faster CI/CD.
3. **TreatWarningsAsErrors** and **Nullable` enabled globally.
4. **Central Package Management** via `Directory.Packages.props` at root.
5. **Zero-Waste Policy**: Immediately delete unused files/dead code.
6. **BFF invariant**: Blazor WASM must never store or handle access tokens. The server authenticates via `HttpOnly`+`SameSite=Strict` cookies and serializes the principal (claims only) to the client via `AddAuthenticationStateSerialization` ↔ `AddAuthenticationStateDeserialization`. The `Microsoft.AspNetCore.Components.WebAssembly.Authentication` package IS referenced on `.Client` (it supplies `AddAuthenticationStateDeserialization`), but only the **safe, claims-only** extension is used — `AddOidcAuthentication()` and any token-handling API in that package are forbidden.

## Auth Modes (BFF)
- Cookie is the default auth + challenge scheme: an unauthenticated page hit redirects to `/login`
  (not straight to Microsoft). The explicit `/auth/*` routes below are the canonical entry points —
  `/auth/login/microsoft` invokes OIDC, `/auth/login/fake` is the dev/test bypass, `/auth/logout` signs
  out (cookie + OIDC), and `/auth/me` returns the server auth state with `returnUrl` validation.
  Legacy `/challenge-microsoft`, `/dev-login`, and `/logout` are still wired as back-compat aliases.
  `/api/*` returns 401.
- The server serializes the cookie principal to the WASM client via `AddAuthenticationStateSerialization`;
  the client reads it via `AddAuthenticationStateDeserialization` — claims only, **no tokens in the browser**.
- **Production**: Microsoft OIDC required. GUEST button hidden/disabled.
- **Development**: Microsoft OIDC + GUEST mode (click "Continue as GUEST").
- **Test env**: `Auth:EnableFakeAuth=true` swaps the entire pipeline for `FakeAuthHandler`
  (header-driven `X-Fake-User`/`X-Fake-Roles`). Defense in depth: the handler also throws
  `InvalidOperationException` if instantiated in Production.
- **GUEST**: Random 8-digit suffix (e.g., GUEST35367543). Persisted via LocalStorage.
- **Dev login**: `/dev-login?email=guest@guest.local` or `/dev-login?guestId=GUEST12345678`
  (legacy aliases preserved for the Login.razor links; canonical is `/auth/login/fake`).
- **Issuer validation**: `ValidateIssuer=false` was replaced with an `IssuerValidator` that accepts
  any `iss` matching `https://login.microsoftonline.com/{tenant}/v2.0`. This preserves the
  multi-tenant + personal-account behavior (`common` authority) while rejecting malformed issuer
  strings a misconfigured `Authority` could otherwise produce.

## API Endpoints
| Endpoint | Description |
|---|---|
| `GET /health` | Health check (JSON) |
| `GET /alive` | Liveness probe |
| `GET /api/diag` | Diagnostics with masked secrets |
| `POST /api/images/analyze` | Image analysis |
| `GET /api/bulk-generate/prompts` | Load saved bulk prompts |
| `POST /api/bulk-generate/prompts` | Save bulk prompts |
| `POST /api/bulk-generate/describe` | GPT-4o vision caption for an image |
| `POST /api/bulk-generate/variation` | Single bulk variation |
| `POST /api/bulk-generate/reroll` | Re-roll N variations from a winning prompt |
| `POST /api/user-images/original` | Save original upload |
| `POST /api/user-images/result` | Save generated result |
| `GET /api/user-images` | List user's saved images |
| `GET /api/meme-templates` | List meme templates |
| `POST /api/meme-templates/render` | Render meme from template |
| `POST /api/style-director/run` | 4-agent style director |
| `GET /auth/login/microsoft?returnUrl=…` | Microsoft OIDC challenge (canonical) |
| `GET /auth/login/fake?email=…` | Dev/Test sign-in (canonical; GUEST or named user) |
| `GET /auth/logout` | Sign out (canonical) |
| `GET /auth/me?returnUrl=…` | Server auth state + returnUrl validation (401 when anonymous) |
| `GET /challenge-microsoft` | Legacy alias for /auth/login/microsoft |
| `GET /dev-login?email=…` | Legacy alias for /auth/login/fake (Development only) |
| `GET /logout` | Legacy alias for /auth/logout |
| `GET /scalar/v1` | Interactive API docs |

## Health & Diagnostics
- `/health` — JSON response for automated pings
- `/diag` — Status of connections with masked keys (hidden page, no UI link)
- Named checks: key-vault, computer-vision, openai, table-storage, imagen3

## Storage Strategy
- **Local**: Azurite (Docker) via `UseDevelopmentStorage=true`
- **Azure**: Table Storage with strongly typed DTOs (ITableEntity)
- **Auto-switch**: Logic toggles between Azurite and production Azure Storage

## Secret Management
1. **Primary**: Azure Key Vault (`kv-poshared`, in the `PoShared` resource group)
2. **Backup**: App Service Application Settings (Key Vault references)
3. **Identity**: System-assigned Managed Identity (`Key Vault Secrets User` on `kv-poshared`)
4. **App resources** (App Service `poredoimage-web`, storage) live in the `PoRedoImage` resource group;
   the shared Basic-B2 plan `asp-poshared-linux` lives in `PoShared`.

## Key Configuration
```json
{
  "KeyVault.Uri": "https://kv-poshared.vault.azure.net/",
  "AzureAd.ClientId": "(set in KV or appsettings)",
  "AzureAd.TenantId": "common",
  "Storage:ConnectionString": "UseDevelopmentStorage=true (local)"
}
```

## UI Conventions
- **Left-aligned top nav bar** with brand + NavMenu + LoginDisplay
- **"USING MOCK DATA" banner** shown when IMockable services active
- **GUEST LOGGED IN** shown in nav bar for guest users
- **Environment badge** in footer for non-production
- **Mobile-first**: CSS clamp() and auto-fit grid

## Testing
- **Target layout (spec §1)**: four projects — `Tests.Unit`, `Tests.Integration`, `Tests.E2E.ApiSmoke`
  (pure HTTP), `Tests.E2E.UI` (C# Playwright). Current state has three: E2EAPI + E2EUI are merged into
  `Tests.E2E` (see ADR-013). The 4-project split is the target — tracked in the audit output.
- **Unit** (`Tests.Unit`): xUnit + Moq. Pure logic, no I/O.
- **Integration** (`Tests.Integration`): xUnit + `Microsoft.AspNetCore.Mvc.Testing` + Testcontainers
  (Azurite, non-AI mock data). Tests own their own throwaway Azurite container — the
  `docker-compose.yml` Azurite is for the local dev inner loop only (F5) and must never be touched
  by tests (see ADR-019).
- **E2E API smoke** (target: `Tests.E2E.ApiSmoke`; currently inside `Tests.E2E`):
  pure HTTP against a running instance, no browser.
- **E2E UI** (target: `Tests.E2E.UI`; currently inside `Tests.E2E`): C# Playwright
  browser tests; class names containing "Ui" identify the UI suite. `[LiveServerFact]` self-skips
  when no live instance is reachable. Browsers install via the generated `playwright.ps1`.
- **Per-tier method ceilings** (100/50/25) are enforced by `TestCountCeilingTests`.
- **CI/CD does NOT run tests** (project policy) — run suites locally or in a dedicated
  non-deploy workflow. `E2E_BASE_URL` overrides the target (default `http://localhost:5000`).
- **Test data**: Non-AI mock data for integration/E2E. Real AI data for local dev.
- **Environment isolation**: integration + E2E tests run with `ASPNETCORE_ENVIRONMENT=Test` so the
  `FakeAuthHandler` and `/auth/login/fake` (Dev+Test only) become available without dev-mode UI.

## AI Models (Rule 14)
Three categories selectable from the Studio home page:
1. **Remote** — Azure OpenAI (gpt-4o chat/vision) + Google Gemini (image generation)
2. **Web Browser** — Models that run client-side via WebGPU/WebAssembly
3. **Ollama** — Local models (dev only, requires Ollama running locally)