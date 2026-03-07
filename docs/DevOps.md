# DevOps Guide: PoRedoImage

## CI/CD Pipeline

GitHub Actions (`.github/workflows/azure-deploy.yml`) runs on every push to `main`:

1. **Restore** — `dotnet restore PoRedoImage.slnx`
2. **Build** — `dotnet build --configuration Release`
3. **Unit Tests** — `dotnet test tests/PoRedoImage.Tests.Unit`
4. **Publish** — `dotnet publish src/PoRedoImage.Web`
5. **Deploy** — `az webapp deploy` via OIDC (no static secrets in CI)
6. **Smoke Test** — `GET /health` must return `Healthy`

## Running Locally

```bash
dotnet run --project src/PoRedoImage.Web
# http://localhost:5000  /  https://localhost:5001
```

Dev auth: navigate to `http://localhost:5000/dev-login?email=you@example.com`

## Secrets Management

| Environment | How |
|-------------|-----|
| Local | `dotnet user-secrets` or `appsettings.Development.json` |
| Production | Azure Key Vault via Managed Identity (`AZURE_KEY_VAULT_ENDPOINT` app setting) |

### Required secrets (Key Vault names → config keys)

| Key Vault Secret | Config Key |
|-----------------|------------|
| `PoRedoImage-ComputerVision-ApiKey` | `ComputerVision:ApiKey` |
| `PoRedoImage-ComputerVision-Endpoint` | `ComputerVision:Endpoint` |
| `PoRedoImage-OpenAI-ApiKey` | `OpenAI:Key` |
| `PoRedoImage-OpenAI-Endpoint` | `OpenAI:Endpoint` |
| `PoRedoImage-StorageConnectionString` | `Storage:ConnectionString` |
| `PoRedoImage-ApplicationInsights-ConnectionString` | `ApplicationInsights:ConnectionString` |
| `PoRedoImage-AzureAd-ClientId` | `AzureAd:ClientId` |
| `PoRedoImage-AzureAd-ClientSecret` | `AzureAd:ClientSecret` |

## Infrastructure Provisioning

```bash
az deployment group create \
  --resource-group PoRedoImage \
  --template-file infra/main.bicep
```

Provisioned resources (see `infra/main.bicep`):
- **Azure App Service** on shared Linux plan from PoShared
- **Azure Storage Account** (Table Storage for bulk prompt persistence)

Shared resources in `PoShared` RG (not provisioned here):
- Azure OpenAI, Computer Vision, Application Insights, Key Vault

## Microsoft OAuth Setup (Production)

Run the helper script *once* to create the App Registration and store credentials in Key Vault:

```powershell
.\scripts\setup-azure-auth.ps1 -WebAppName "poredoimage-web"
```

## Observability

- **Structured logs**: Serilog → Console (dev) + File (`logs/`) + Application Insights (prod)
- **Traces & metrics**: OpenTelemetry → Azure Monitor (`UseAzureMonitor()`)
- **Health checks**: `/health` (detailed JSON) and `/alive` (liveness probe)
- **Diagnostics**: `/diag` page shows masked config values at runtime

## E2E Tests

```bash
cd tests/PoRedoImage.Tests.E2E
npm install
npx playwright install chromium

# Run against local server (must be running on :5000)
npx playwright test

# Run headed
npx playwright test --headed
```
