# PoRedoImage — AI Agent Context

## Overview
PoRedoImage is a Blazor WebAssembly (global Interactive WebAssembly, no prerender) SPA for AI-powered
image analysis, regeneration, meme generation, and bulk generation, hosted behind an ASP.NET Core
Backend-for-Frontend (BFF). The `.Web` project is the API/BFF host only; all interactive UI (router,
layout, pages) lives in and runs from the `.Client` WASM assembly. The server side uses Onion layering
(Domain → Application → Infrastructure); the UI is a single WASM client.

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
├── PoRedoImage.Domain/       # Entities, Interfaces (innermost layer)
├── PoRedoImage.Application/  # Service orchestration
├── PoRedoImage.Infrastructure/ # Repositories, external services
├── PoRedoImage.Shared/       # DTOs + FluentValidation shared between WASM and API
├── PoRedoImage.Client/       # Blazor WASM SPA — Routes, Layout, Pages, Shared, Models, wwwroot
└── PoRedoImage.Web/          # API/BFF host (App.razor host doc + minimal-API endpoints)
tests/
├── PoRedoImage.Tests.Unit/        # Unit tests (xUnit + Moq)
├── PoRedoImage.Tests.Integration/ # Integration tests (Testcontainers — Azurite)
└── PoRedoImage.Tests.E2E/         # End-to-end: HTTP API smoke + C# Playwright UI (self-skip if no live instance)
```
> Render model: only `.Client` ships to the browser, so every interactive component lives there.
> `.Web` holds just `Components/App.razor` (host document) + `_Imports.razor`; it renders the Client's
> `<Routes>`/`<HeadOutlet>` with `RenderModes.WasmNoPrerender`.

## Key Architecture Rules
1. **Onion (server) + WASM/BFF (client)**: Domain → Application → Infrastructure → Web for the backend; the UI is a single Blazor WASM client behind the `.Web` BFF. No inward dependencies.
2. **No AOT**: Blazor WASM uses interpreted mode for smaller builds and faster CI/CD.
3. **TreatWarningsAsErrors** and **Nullable** enabled globally.
4. **Central Package Management** via `Directory.Packages.props` at root.
5. **Zero-Waste Policy**: Immediately delete unused files/dead code.

## Auth Modes (BFF)
- Cookie is the default auth + challenge scheme: an unauthenticated page hit redirects to `/login`
  (not straight to Microsoft). `/challenge-microsoft` invokes OIDC explicitly. `/api/*` returns 401.
- The server serializes the cookie principal to the WASM client via `AddAuthenticationStateSerialization`;
  the client reads it via `AddAuthenticationStateDeserialization` — claims only, **no tokens in the browser**.
- **Production**: Microsoft OIDC required. GUEST button hidden/disabled.
- **Development**: Microsoft OIDC + GUEST mode (click "Continue as GUEST").
- **GUEST**: Random 8-digit suffix (e.g., GUEST35367543). Persisted via LocalStorage.
- **Dev login**: `/dev-login?email=guest@guest.local` or `/dev-login?guestId=GUEST12345678`

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
| `POST /api/caption-battle/run` | Multi-persona caption battle |
| `POST /api/style-director/run` | 4-agent style director |
| `GET /dev-login?email=…` | Dev/Test sign-in (Development only) |
| `GET /challenge-microsoft` | Microsoft OIDC challenge |
| `GET /logout` | Sign out (both environments) |
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
- **Unit**: xUnit + Moq (dev/test data)
- **Integration**: Testcontainers (Azurite, non-AI mock data)
- **E2E** (`PoRedoImage.Tests.E2E`): one project holding both pure HTTP API smoke tests and C# Playwright
  browser/UI tests against a running instance (`[LiveServerFact]` self-skips when down). UI vs API suites are
  distinguished by convention — class names containing "Ui" are the UI suite. Install browsers via the
  generated `playwright.ps1`. Per-tier method ceilings (100/50/25) are enforced by `TestCountCeilingTests`.
- **CI/CD does NOT run tests** (project policy) — run suites locally. `E2E_BASE_URL` overrides the target (default `http://localhost:5000`).
- **Test data**: Non-AI mock data for integration/E2E. Real AI data for local dev.

## AI Models (Rule 14)
Three categories selectable from the Studio home page:
1. **Remote** — Azure OpenAI (gpt-4o chat/vision) + Google Gemini (image generation)
2. **Web Browser** — Models that run client-side via WebGPU/WebAssembly
3. **Ollama** — Local models (dev only, requires Ollama running locally)