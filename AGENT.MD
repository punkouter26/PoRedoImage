# PoRedoImage — AI Agent Context

## Overview
PoRedoImage is a Blazor WebAssembly + Interactive Server application for AI-powered image analysis, regeneration, meme generation, and bulk image generation. It follows strict Onion Architecture with physical project separation.

## Quick Facts
- **Solution**: PoRedoImage (.slnx format)
- **Master Prefix**: `PoRedoImage` for namespaces, Azure resources
- **Framework**: .NET 10 (pinned via global.json)
- **Client**: Blazor WASM (hosted by server) + Radzen UI
- **Server**: Interactive Server rendering with WebAssembly fallback
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
├── PoRedoImage.Shared/       # DTOs shared between WASM and API
├── PoRedoImage.Client/       # Blazor WASM client (wwwroot belongs here)
└── PoRedoImage.Web/          # Host server (Components, API endpoints)
tests/
├── PoRedoImage.Tests.Unit/   # Unit tests (xUnit + Moq)
└── PoRedoImage.Tests.Integration/ # Integration tests (Testcontainers)
```

## Key Architecture Rules
1. **Onion Architecture**: Domain → Application → Infrastructure → Web. No inward dependencies.
2. **No AOT**: Blazor WASM uses interpreted mode for smaller builds and faster CI/CD.
3. **TreatWarningsAsErrors** and **Nullable** enabled globally.
4. **Central Package Management** via `Directory.Packages.props` at root.
5. **Zero-Waste Policy**: Immediately delete unused files/dead code.

## Auth Modes
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
| `POST /api/bulk/generate` | Bulk generation |

## Health & Diagnostics
- `/health` — JSON response for automated pings
- `/diag` — Status of connections with masked keys (hidden page, no UI link)
- Named checks: key-vault, computer-vision, openai, table-storage, imagen3

## Storage Strategy
- **Local**: Azurite (Docker) via `UseDevelopmentStorage=true`
- **Azure**: Table Storage with strongly typed DTOs (ITableEntity)
- **Auto-switch**: Logic toggles between Azurite and production Azure Storage

## Secret Management
1. **Primary**: Azure Key Vault (`kv-poshared`)
2. **Backup**: App Service Application Settings
3. **Identity**: Managed Identity (Punkouter26 resource group)

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
- **E2E**: Playwright (TypeScript) in `tests/playwright/`
- **Test data**: Non-AI mock data for integration/E2E. Real AI data for local dev.

## AI Models (Rule 14)
Three categories selectable from the Studio home page:
1. **Remote** — Azure OpenAI models (gpt-4o, dall-e-3)
2. **Web Browser** — Models that run client-side via WebGPU/WebAssembly
3. **Ollama** — Local models (dev only, requires Ollama running locally)

## Available Skills (Copilot)
See `.github/copilot-instructions.md` for detailed skill instructions:
- `architecture-blueprint-generator`
- `dotnet-best-practices`
- `dotnet-design-pattern-review`
- `autoresearch`
- `security-review`
- `appinsights-instrumentation`
- `azure-deployment-preflight`
- `create-readme`
- `repo-story-time`