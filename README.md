# PoRedoImage - AI-Powered Image Analysis Platform

An intelligent image analysis application leveraging Azure AI services to analyze images, generate enhanced descriptions, and create new images. Built with modern .NET architecture, secure secret management, and comprehensive observability.

## 🌐 Live Application

**Production URL**: [https://app-poredoimage-cqevadpy77pvi.azurewebsites.net](https://app-poredoimage-cqevadpy77pvi.azurewebsites.net)

- **Resource Group**: poredoimage-uksouth
- **Region**: UK South  
- **App Service Plan**: PoShared5 (F1 Free tier)
- **Platform**: Azure App Service (.NET 10.0)
- **Security**: Azure Key Vault, Managed Identity

## �🎯 Application Overview

PoRedoImage is a modern web application that demonstrates the power of AI-driven image analysis and generation. Users can upload images to receive detailed AI-generated descriptions and see new images created based on those descriptions, showcasing the fascinating capabilities of computer vision and generative AI working together.

### Key Capabilities
- **🔍 Image Analysis**: Advanced computer vision analysis of uploaded images
- **📝 Enhanced Descriptions**: AI-powered generation of detailed, contextual descriptions  
- **🎨 Image Regeneration**: DALL-E powered creation of new images from descriptions
- **📊 Performance Metrics**: Real-time tracking of processing times and token usage
- **🏥 Health Monitoring**: Comprehensive diagnostics and Azure service connectivity checks
- **🌐 Modern UI**: Responsive Blazor WebAssembly interface with real-time updates

## 🏗️ Architecture

The application follows a clean, modern architecture pattern:

- **Frontend**: Blazor WebAssembly client with responsive UI
- **Backend**: ASP.NET Core API with dependency injection and logging
- **Shared**: Common models and contracts for type safety
- **Testing**: Comprehensive test suite with integration and unit tests

### 📊 Architecture Diagrams

Visual representations of the system architecture are available in the [`/Diagrams`](./Diagrams) folder:

- **[Project Dependencies](./Diagrams/project-dependency-diagram.svg)** - Shows how projects and Azure services connect
- **[Domain Model](./Diagrams/class-diagram-domain-entities.svg)** - Core business objects and relationships  
- **[API Flow](./Diagrams/sequence-diagram-api-calls.svg)** - Request/response flow through the system
- **[User Journey](./Diagrams/flowchart-use-case.svg)** - Complete user experience workflow
- **[UI Components](./Diagrams/component-hierarchy-diagram.svg)** - Blazor component structure

*View the [Diagrams README](./Diagrams/README.md) for detailed information about each diagram.*

## CI/CD Status

[![Build and Test](https://github.com/punkouter25/PoRedoImage/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/punkouter25/PoRedoImage/actions/workflows/build-and-test.yml)
[![Deploy to Azure](https://github.com/punkouter25/PoRedoImage/actions/workflows/deploy-to-azure.yml/badge.svg)](https://github.com/punkouter25/PoRedoImage/actions/workflows/deploy-to-azure.yml)
[![Security Scan](https://github.com/punkouter25/PoRedoImage/actions/workflows/security-scan.yml/badge.svg)](https://github.com/punkouter25/PoRedoImage/actions/workflows/security-scan.yml)

## 📋 UI Pages & Components

### Core Pages
- **🏠 Home (/)**: Main image upload and analysis interface
  - File upload component with drag-and-drop support
  - Image preview and configuration options  
  - Real-time processing indicators
  - Results display with enhanced descriptions, tags, and metrics
  - Generated image showcase

- **🔧 Diagnostics (/diag)**: System health and connectivity dashboard
  - Azure service connectivity tests
  - Performance metrics and logs
  - Configuration validation
  - Troubleshooting utilities

### Component Hierarchy
- **App.razor**: Root application component with routing
- **MainLayout.razor**: Master layout with navigation and content areas
- **NavMenu.razor**: Navigation component with responsive menu
- **Home.razor**: Primary feature page with multiple UI sections
- **Diag.razor**: Diagnostics and monitoring dashboard

*See the [Component Hierarchy Diagram](./Diagrams/component-hierarchy-diagram.svg) for detailed UI structure.*

## ⚡ Features

- **🔍 Image Analysis**: Azure Computer Vision powered image analysis
- **🤖 Enhanced Descriptions**: Azure OpenAI GPT-4 generated detailed descriptions
- **🎨 Image Generation**: DALL-E powered image creation from descriptions
- **📱 Responsive UI**: Modern Blazor WebAssembly client interface
- **🔧 RESTful API**: ASP.NET Core backend with comprehensive endpoints
- **📊 Performance Tracking**: Real-time metrics and telemetry collection
- **🏥 Health Monitoring**: Built-in diagnostics and service connectivity checks
- **🔄 CI/CD Pipeline**: Automated testing, building, and deployment
- **🐳 Containerized**: Docker support for consistent deployments
- **☁️ Azure Ready**: Optimized for Azure App Service hosting

## 🛠️ Technology Stack

### Core Framework
- **.NET 10.0** - Latest .NET with improved performance and modern C# features
- **Blazor WebAssembly** - Client-side web UI with C#
- **ASP.NET Core** - High-performance backend API
- **Centralized Package Management** - Single source of truth for dependencies

### Azure Services
- **Azure Key Vault** - Secure secret management with RBAC
- **Azure Computer Vision** - Advanced image analysis
- **Azure OpenAI Service** - GPT-4 descriptions, DALL-E image generation
- **Azure App Service** - PaaS hosting with Managed Identity
- **Application Insights** - Telemetry and monitoring

### Observability & Logging
- **OpenTelemetry** - Modern, vendor-neutral metrics and tracing
- **Serilog** - Structured logging to Console, File, and Application Insights
- **Custom Metrics** - Business-specific telemetry (processing time, token usage)

### DevOps & Infrastructure
- **GitHub Actions** - CI/CD with OIDC federation
- **Azure Developer CLI (azd)** - Infrastructure deployment
- **Bicep** - Infrastructure as Code
- **Docker** - Containerization support

### Testing
- **xUnit** - Unit and integration testing
- **bUnit** - Blazor component testing
- **Moq** - Mocking framework
- **Playwright** - End-to-end browser testing
- **dotnet-coverage** - Code coverage analysis

## CI/CD Pipeline

The project uses GitHub Actions for continuous integration and deployment:

1. **Build and Test**: Builds the solution and runs tests
2. **Deploy to Azure**: Deploys the Server application to Azure App Service
3. **Docker Build**: Builds and pushes Docker images to GitHub Container Registry
4. **Security Scan**: Performs security scanning and vulnerability checks

To deploy the application to your Azure resources, see the [GitHub Actions Deployment Guide](./docs/github-actions-deployment-guide.md) for setup instructions.

## 🚀 Getting Started

### Prerequisites
- **.NET 10.0 SDK** (10.0.100 or later)
- **Node.js 18+** and **npm** (for E2E tests)
- **Azure Subscription** with:
  - Azure Computer Vision
  - Azure OpenAI Service
  - Azure Key Vault (auto-created by deployment)
- **Azure CLI** and **Azure Developer CLI (azd)**
- **Git** for version control

### 🏃‍♂️ Local Development Setup

#### 1. Clone the Repository
```bash
git clone https://github.com/punkouter26/PoRedoImage.git
cd PoRedoImage
```

#### 2. Restore Dependencies
```bash
dotnet restore
```

#### 3. Configure Secrets (Development)

**Option A: Use PowerShell Script (Recommended)**
```powershell
.\scripts\Configure-UserSecrets.ps1
```

**Option B: Manual Configuration**
```bash
cd Server

# Initialize user secrets
dotnet user-secrets init

# Add secrets
dotnet user-secrets set "ApplicationInsights:ConnectionString" "InstrumentationKey=..."
dotnet user-secrets set "ConnectionStrings:AzureTableStorage" "UseDevelopmentStorage=true"
dotnet user-secrets set "ComputerVision:Endpoint" "https://..."
dotnet user-secrets set "ComputerVision:ApiKey" "your-key"
dotnet user-secrets set "OpenAI:Endpoint" "https://..."
dotnet user-secrets set "OpenAI:Key" "your-key"
```

See [docs/SECRETS.md](docs/SECRETS.md) for comprehensive secret management guide.

#### 4. Start Azurite (Local Storage Emulator)
```bash
azurite --location ./AzuriteData
```

#### 5. Build and Run
```bash
# Build the solution
dotnet build

# Run the server (F5 in VS Code also works)
cd Server
dotnet run
```

#### 6. Access the Application
- **App**: `https://localhost:5001`
- **Swagger**: `https://localhost:5001/swagger`
- **Health Check**: `https://localhost:5001/api/health`
- **Diagnostics**: `https://localhost:5001/diag`

### 🐳 Docker Deployment

```bash
# Build the Docker image
docker build -t imagegc:latest .

# Run the container
docker run -p 5000:80 -e ComputerVision__ApiKey=your-key imagegc:latest
```

### ☁️ Azure Deployment

Deploy using Azure Developer CLI with automated infrastructure provisioning.

#### 1. Login to Azure
```bash
az login
azd auth login
```

#### 2. Deploy Infrastructure and Application
```bash
# One command to deploy everything
azd up

# Or step by step:
azd provision  # Create Azure resources
azd deploy     # Deploy application code
```

This creates:
- ✅ Resource Group (poredoimage-{location})
- ✅ Azure Key Vault with RBAC
- ✅ App Service with Managed Identity
- ✅ Application Insights & Log Analytics
- ✅ Storage Account (Azure Tables)
- ✅ Cost Budget ($5/month with 80% alert)

#### 3. Configure Secrets in Key Vault
```powershell
# Get Key Vault name from deployment output
$keyVaultName = azd env get-values | Select-String "AZURE_KEY_VAULT_NAME" | ...

# Run the script to add secrets
.\scripts\Add-SecretsToKeyVault.ps1 -KeyVaultName $keyVaultName
```

#### 4. Verify Deployment
```bash
# Check deployment status
azd monitor

# View application logs
azd logs
```

See [docs/SECRETS.md](docs/SECRETS.md) for detailed secret management and [AGENTS.md](AGENTS.md) for deployment chronicles.

## 🧪 Testing

### Unit and Integration Tests (.NET)
```bash
# Run all .NET tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run specific test categories
dotnet test --filter Category=Integration
```

### End-to-End Tests (Playwright)
```bash
# Run Playwright tests
npm test

# Run with UI mode
npm run test:ui

# Run in headed mode (see browser)
npm run test:headed

# Debug tests
npm run test:debug

# View test report
npm run test:report
```

The Playwright test suite includes:
- ✅ Health endpoint verification
- ✅ Page load and navigation tests
- ✅ Upload functionality verification
- ✅ API endpoint testing
- ✅ HTTPS configuration validation
- ✅ Diagnostics page testing
- ✅ Performance benchmarking
- ✅ CORS and security headers

## 📊 Monitoring & Observability

### Telemetry Stack
- **OpenTelemetry**: Modern metrics and distributed tracing
- **Application Insights**: Azure-native telemetry backend
- **Serilog**: Structured logging to multiple sinks
- **Health Checks**: `/api/health` endpoint for liveness/readiness

### Key Metrics
- `poredoimage.processing.duration` - Image processing time (histogram)
- `poredoimage.openai.tokens.total` - Token consumption tracking (counter)
- `poredoimage.vision.api.calls` - Computer Vision usage (counter)
- `poredoimage.processing.failures` - Error rate monitoring (counter)

### Monitoring Queries
Essential KQL queries are available in:
- [Server/KqlQueries.cs](Server/KqlQueries.cs) - Embedded queries for Application Insights
- [MONITORING.md](MONITORING.md) - Comprehensive monitoring guide

### Diagnostics
Visit `/diag` for real-time system diagnostics:
- Azure service connectivity tests
- Configuration validation
- Performance metrics dashboard
- Recent logs and errors

## 📚 Documentation

- **[docs/SECRETS.md](docs/SECRETS.md)** - Complete secret management guide
- **[docs/adr/](docs/adr/)** - Architecture Decision Records
- **[AGENTS.md](AGENTS.md)** - AI-assisted development chronicle
- **[MONITORING.md](MONITORING.md)** - Observability and monitoring
- **[PRD.md](PRD.md)** - Product Requirements Document
- **[Diagrams/](Diagrams/)** - Architecture diagrams (Mermaid)

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## 📝 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

- **Azure AI Services** for powerful computer vision and language capabilities
- **Microsoft Blazor** for enabling rich web applications with C#
- **GitHub Actions** for seamless CI/CD automation
- **Mermaid** for beautiful architecture diagrams

---

*For detailed technical documentation, architecture diagrams, and development guides, explore the [`/Diagrams`](./Diagrams) folder and project wiki.*
