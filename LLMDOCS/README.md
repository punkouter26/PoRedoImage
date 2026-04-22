# PoRedoImage — LLM Quick Reference

## Solution Structure
```
PoRedoImage.slnx
src/
  PoRedoImage.Domain/         # Entities, domain interfaces (no external deps)
  PoRedoImage.Application/    # Orchestrators / use-cases (refs Domain + Shared)
  PoRedoImage.Infrastructure/ # Azure SDK implementations (refs Application + Domain + Shared)
  PoRedoImage.Shared/         # DTOs shared between Server & Client WASM
  PoRedoImage.Client/         # Blazor WASM frontend (Radzen UI, refs Shared)
  PoRedoImage.Web/            # ASP.NET Core host: serves WASM + Minimal API (refs Infrastructure + Shared + Client)
tests/
  PoRedoImage.Tests.Unit/     # xUnit — domain logic, pure C#
  PoRedoImage.Tests.Integration/ # xUnit + Testcontainers (Azurite) — API/DB
  playwright/                 # Playwright TS — E2E critical paths
```

## Architecture
- **Onion Architecture**: Domain ← Application ← Infrastructure ← Web
- **Client WASM hosted by Web** (server project): `AddInteractiveWebAssemblyComponents()`
- **No domain logic in Web project** — thin endpoint wrappers only
- **All service interfaces in Domain** — Infrastructure contains concrete Azure SDK impls
- **Shared DTOs** (`PoRedoImage.Shared`) are the API contract between WASM client and server

## Key Patterns Used
| Pattern | Where |
|---|---|
| Factory Method | `ImageAnalysis.Create()` in Domain |
| Repository | `IBulkPromptRepository` → `AzureTableBulkPromptRepository` |
| Adapter | `AzureVisionService`, `AzureOpenAiService`, `GeminiImagen3Service` |
| Strategy | `ProcessingMode` enum — determines pipeline branch in orchestrator |
| Extension Method | `InfrastructureServiceExtensions.AddPoRedoImageInfrastructure()` |

## API Endpoints
| Method | Route | Description |
|---|---|---|
| POST | `/api/images/analyze` | Analyze + regenerate or meme an image |
| GET | `/api/images/health` | Image analysis service health |
| GET | `/api/diag` | Masked config/connection diagnostics |
| GET | `/health` | Full health check (JSON) |
| GET | `/alive` | Liveness probe |
| GET | `/scalar/v1` | OpenAPI Scalar UI |
| GET | `/dev-login` | Dev-only cookie sign-in (`?email=anon@anon.local` for ANON) |

## Authentication
- **Dev (no AzureAd:ClientId set)**: cookie-only; `/login` shows dev email form + ANON button
- **ANON user**: `email=anon@anon.local` → `NameIdentifier=anon|ANON` — all DB writes tagged to ANON account
- **Prod**: Microsoft OIDC via `/challenge-microsoft`
- E2E tests use `/dev-login?email=anon@anon.local` to bypass OAuth

## Configuration Hierarchy
1. `appsettings.json` — defaults (no secrets)
2. `appsettings.{Environment}.json` — env overrides
3. `dotnet user-secrets` — local dev secrets
4. Azure Key Vault (`AZURE_KEY_VAULT_ENDPOINT`) — prod secrets via Managed Identity

## Key Config Keys
| Key | Purpose |
|---|---|
| `AZURE_KEY_VAULT_ENDPOINT` | Key Vault URL (PoShared) |
| `ComputerVision:Endpoint` | Azure AI Vision endpoint |
| `ComputerVision:ApiKey` | Azure AI Vision key |
| `OpenAI:Endpoint` | Azure OpenAI endpoint |
| `OpenAI:Key` | Azure OpenAI key |
| `Google:ApiKey` | Gemini API key |
| `Storage:ConnectionString` | Azure Table Storage / Azurite |
| `ApplicationInsights:ConnectionString` | App Insights (from Key Vault) |

## Feature Flags (`FeatureFlags:*` in appsettings)
| Flag | Default | Purpose |
|---|---|---|
| `UseRealAiInDev` | `true` | Use real AI when running locally |
| `UseRealAiInIntegrationTests` | `false` | Use mocks in integration tests |
| `EnableBulkGenerate` | `true` | Bulk image generation feature |
| `EnableMemeGeneration` | `true` | Meme caption + overlay feature |
| `EnableImagen3` | `true` | Google Imagen 3 / Gemini generation |

## Local Development
```bash
# Start Azurite (Azure Table Storage emulator)
docker-compose -f docker-compose.azurite.yml up -d

# Set secrets
dotnet user-secrets set "ComputerVision:Endpoint" "https://..." --project src/PoRedoImage.Web
dotnet user-secrets set "ComputerVision:ApiKey" "..." --project src/PoRedoImage.Web
dotnet user-secrets set "OpenAI:Endpoint" "https://..." --project src/PoRedoImage.Web
dotnet user-secrets set "OpenAI:Key" "..." --project src/PoRedoImage.Web
dotnet user-secrets set "Storage:ConnectionString" "UseDevelopmentStorage=true" --project src/PoRedoImage.Web

# Run
dotnet run --project src/PoRedoImage.Web
# → http://localhost:5000 / https://localhost:5001
```

## Testing
```bash
dotnet test PoRedoImage.slnx
npx playwright test  # from tests/playwright/
```

## Ports
- HTTP: `5000`  HTTPS: `5001`

## Azure Resources
- App Service Plan: PoShared resource group
- App Service: PoRedoImage resource group
- Table Storage: PoRedoImage resource group
- Key Vault: PoShared resource group (`kv-poshared`)
- App Insights: PoShared resource group
- Subscription: `Punkouter26 (Bbb8dfbe-9169-432f-9b7a-fbf861b51037)`
