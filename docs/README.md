# PoImageGc

A .NET 10 Unified Blazor Web App with Aspire for intelligent image analysis, description generation, and meme creation.

## Overview

PoImageGc is an image processing application that leverages:
- **Azure Computer Vision** for image analysis (tags, objects, descriptions)
- **Azure OpenAI** for enhanced natural language descriptions and meme captions
- **DALL-E** for AI-generated meme images

## Architecture

```
PoImageGc/
├── src/
│   ├── PoImageGc.AppHost/        # Aspire orchestration host
│   ├── PoImageGc.ServiceDefaults/ # Shared Aspire defaults (OpenTelemetry, health)
│   ├── PoImageGc.Web/            # Unified Blazor Web App (server)
│   ├── PoImageGc.Web.Client/     # Interactive WASM components
│   └── PoImageGc.Shared/         # DTOs and shared models
├── tests/
│   ├── PoImageGc.Tests.Unit/     # Unit tests (xUnit)
│   ├── PoImageGc.Tests.Integration/ # Integration tests
│   └── PoImageGc.Tests.E2E/      # End-to-end tests (Playwright)
├── docs/                          # Documentation and KQL queries
└── infra/                         # Bicep infrastructure-as-code
```

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js 18+](https://nodejs.org/) (for E2E tests)
- [Azure CLI](https://docs.microsoft.com/cli/azure/install-azure-cli)
- [Azure Developer CLI (azd)](https://learn.microsoft.com/azure/developer/azure-developer-cli/install-azd)
- Visual Studio Code with C# Dev Kit extension

## Getting Started

### 1. Clone and Restore

```bash
git clone <repository-url>
cd PoRedoImage
dotnet restore PoImageGc.sln
```

### 2. Configure Secrets

For local development, configure the following in `appsettings.Development.json` or User Secrets:

```json
{
  "AzureComputerVision": {
    "Endpoint": "https://your-vision.cognitiveservices.azure.com/",
    "Key": "your-key"
  },
  "AzureOpenAI": {
    "Endpoint": "https://your-openai.openai.azure.com/",
    "Key": "your-key",
    "DeploymentName": "gpt-4"
  },
  "AzureStorage": {
    "ConnectionString": "UseDevelopmentStorage=true"
  }
}
```

For production, secrets are stored in Azure Key Vault.

### 3. Run the Application

Using Aspire (recommended):
```bash
dotnet run --project src/PoImageGc.AppHost
```

Or directly:
```bash
dotnet run --project src/PoImageGc.Web
```

Press **F5** in VS Code to debug with the configured launch configuration.

### 4. Open the Application

Navigate to `https://localhost:5001` in your browser.

## Development

### Building

```bash
dotnet build PoImageGc.sln
```

### Running Tests

**Unit Tests:**
```bash
dotnet test tests/PoImageGc.Tests.Unit
```

**Integration Tests:**
```bash
dotnet test tests/PoImageGc.Tests.Integration
```

**E2E Tests:**
```bash
cd tests/PoImageGc.Tests.E2E
npm install
npx playwright install chromium
npm test
```

### Code Quality

Format code:
```bash
dotnet format PoImageGc.sln
```

## API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/health` | GET | Health check with dependencies |
| `/api/images/analyze` | POST | Analyze image (URL or base64) |
| `/health` | GET | Basic health status |
| `/alive` | GET | Liveness probe |

Use the `PoImageGc.http` file to test endpoints directly in VS Code.

## Deployment

### Deploy to Azure

```bash
azd init
azd up
```

This provisions:
- Azure App Service
- Application Insights + Log Analytics
- Azure Key Vault
- Azure Storage

### GitHub Actions

CI/CD is configured to use OIDC federated credentials for secure deployment without secrets.

## Project Structure

### Render Modes

- **Server-side rendering (SSR)**: Default for all pages
- **InteractiveAuto**: Used for the Home page image processing UI (high responsiveness)

### Processing Modes

1. **Analyze**: Extract tags, objects, and basic description using Computer Vision
2. **Describe**: Generate detailed AI-powered descriptions using OpenAI
3. **Meme**: Create humorous captions and optionally generate meme images

## Monitoring

### Application Insights

View metrics, logs, and traces in Application Insights. Essential KQL queries are in `docs/kql/`.

### Aspire Dashboard

When running with AppHost, access the Aspire dashboard at `https://localhost:15000` for:
- Distributed traces
- Metrics
- Logs
- Resource health

## License

MIT License - see LICENSE file for details.
