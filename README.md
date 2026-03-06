# PoRedoImage - AI-Powered Image Analysis Platform

An intelligent image analysis application leveraging Azure AI services to analyze images, generate enhanced descriptions, and create new images. Built with .NET 10 Unified Blazor Web App architecture and .NET Aspire for orchestration.

## 🌐 Live Application

**Production URL**: TBD after deployment

## 🎯 Application Overview

PoRedoImage demonstrates the power of AI-driven image analysis and generation. Users can upload images to receive detailed AI-generated descriptions and see new images created based on those descriptions.

### Key Capabilities
- 🔍 **Image Analysis**: Azure Computer Vision powered analysis
- 📝 **Enhanced Descriptions**: Azure OpenAI GPT-4 generated descriptions  
- 🎨 **Image Regeneration**: DALL-E powered image creation
- 🃏 **Meme Generation**: AI-generated captions with image overlay
- 📊 **Performance Metrics**: Real-time processing time tracking
- 🏥 **Health Monitoring**: Aspire health endpoints with OpenTelemetry

## 🏗️ Architecture

The application follows .NET 10 Unified Blazor Web App architecture with Vertical Slice organization:

```
src/
├── PoRedoImage.Web/              # Main Blazor Web App (Server)
│   ├── Components/             # Blazor components (SSR default)
│   └── Features/               # Vertical slices
│       └── ImageAnalysis/      # Image analysis feature
│           ├── ComputerVisionService.cs
│           ├── OpenAIService.cs
│           ├── MemeGeneratorService.cs
│           └── ImageAnalysisEndpoints.cs  # Minimal APIs
├── PoRedoImage.Web.Client/       # Blazor WASM (Interactive)
├── PoRedoImage.AppHost/          # Aspire orchestration
├── PoRedoImage.ServiceDefaults/  # OpenTelemetry, health checks
└── PoRedoImage.Shared/           # DTOs and contracts

tests/
├── PoRedoImage.Tests.Unit/       # Unit tests
└── PoRedoImage.Tests.Integration/ # Integration tests
```

### Key Design Decisions
- **Unified Blazor**: Single project for both server-rendered and WASM components
- **InteractiveAuto**: SSR by default, interactive components where needed
- **Vertical Slice**: Feature-based organization, not layer-based
- **Minimal APIs**: No controllers, endpoints defined with `MapGroup`
- **Aspire**: Service orchestration, OpenTelemetry, health checks

## 🛠️ Technology Stack

### Core Framework
- **.NET 10.0** - Latest .NET with modern C# features
- **Blazor Web App** - Unified rendering (SSR + WASM)
- **.NET Aspire 9.3** - Service orchestration and observability
- **Central Package Management** - Directory.Packages.props

### Azure Services
- **Azure Key Vault** - Secret management (Production)
- **Azure Computer Vision** - Image analysis
- **Azure OpenAI Service** - GPT-4 descriptions, DALL-E generation
- **Application Insights** - Telemetry and monitoring

### Observability
- **OpenTelemetry** - Traces, metrics, logs
- **Serilog** - Structured logging
- **Application Insights** - Azure integration

### Testing
- **xUnit** - Test framework
- **Moq** - Mocking
- **WebApplicationFactory** - Integration testing

## 🚀 Getting Started

### Prerequisites
- .NET 10.0 SDK (10.0.100+)
- Azure Subscription with Computer Vision and OpenAI services
- VS Code with C# Dev Kit extension

### Local Development

#### 1. Clone and Restore
```bash
git clone https://github.com/punkouter26/PoRedoImage.git
cd PoRedoImage
dotnet restore PoRedoImage.slnx
```

#### 2. Configure Secrets

Create or update `src/PoRedoImage.Web/appsettings.Development.json`:
```json
{
  "AzureOpenAI": {
    "ApiKey": "your-key",
    "Endpoint": "https://your-resource.openai.azure.com/",
    "DeploymentName": "gpt-4",
    "DalleDeploymentName": "dall-e-3"
  },
  "ComputerVision": {
    "Key": "your-key",
    "Endpoint": "https://your-resource.cognitiveservices.azure.com/"
  }
}
```

#### 3. Run the Application

**Option A: Direct (Web only)**
```bash
cd src/PoRedoImage.Web
dotnet run
```

**Option B: With Aspire AppHost**
```bash
cd src/PoRedoImage.AppHost
dotnet run
```

**Option C: VS Code F5**
- Select "Launch Web (Direct)" or "Launch AppHost (Aspire)"

### Running Tests

```bash
# All tests
dotnet test PoRedoImage.slnx

# Unit tests only
dotnet test tests/PoRedoImage.Tests.Unit

# Integration tests only
dotnet test tests/PoRedoImage.Tests.Integration

# With coverage
dotnet test PoRedoImage.slnx --collect:"XPlat Code Coverage"
```

## 📡 API Endpoints

### Health Checks (Aspire Defaults)
- `GET /health` - Full health check
- `GET /alive` - Liveness probe

### Image Analysis (Minimal API)
- `GET /api/images/status` - Service status
- `POST /api/images/analyze` - Analyze image

### Documentation
- `GET /openapi/v1.json` - OpenAPI spec
- `GET /scalar/v1` - Interactive API docs

## 📁 Project Structure

| Project | Purpose |
|---------|---------|
| `PoRedoImage.Web` | Main Blazor Web App, hosts API and UI |
| `PoRedoImage.Web.Client` | WASM client for interactive components |
| `PoRedoImage.AppHost` | Aspire orchestration host |
| `PoRedoImage.ServiceDefaults` | Shared Aspire config (OpenTelemetry, health) |
| `PoRedoImage.Shared` | DTOs shared between Web and Client |
| `PoRedoImage.Tests.Unit` | Unit tests with xUnit |
| `PoRedoImage.Tests.Integration` | Integration tests with WebApplicationFactory |

## 🔧 Configuration

### Environment Variables
- `ASPNETCORE_ENVIRONMENT` - Development/Production
- `AZURE_KEY_VAULT_ENDPOINT` - Key Vault URI (Production)

### appsettings.json Structure
```json
{
  "Logging": { ... },
  "AzureOpenAI": {
    "ApiKey": "",
    "Endpoint": "",
    "DeploymentName": "gpt-4",
    "DalleDeploymentName": "dall-e-3"
  },
  "ComputerVision": {
    "Key": "",
    "Endpoint": ""
  },
  "ApplicationInsights": {
    "ConnectionString": ""
  }
}
```

## 📝 Development Guidelines

Following [copilot-instructions.md](.github/copilot-instructions.md):

- **Vertical Slice Architecture** - Features in `/Features/{FeatureName}/`
- **Minimal APIs** - No controllers, use `MapGroup`
- **Central Package Management** - All versions in Directory.Packages.props
- **Test Naming** - `MethodName_StateUnderTest_ExpectedBehavior`
- **TDD Workflow** - Red → Green → Refactor
- **80% Code Coverage** - Target for business logic

## 📄 License

MIT License - See LICENSE file for details.
