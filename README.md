# ImageGc - Image Analysis with Azure AI

An intelligent image analysis application that uses Azure AI services to analyze images and generate enhanced descriptions. The project uses Azure Computer Vision to analyze images and Azure OpenAI to enhance descriptions and generate images based on analysis.

## 🎯 Application Overview

ImageGc is a modern web application that demonstrates the power of AI-driven image analysis and generation. Users can upload images to receive detailed AI-generated descriptions and see new images created based on those descriptions, showcasing the fascinating capabilities of computer vision and generative AI working together.

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

- **Frontend**: Blazor WebAssembly, Bootstrap, JavaScript
- **Backend**: ASP.NET Core 9.0, C#, Dependency Injection
- **AI Services**: Azure Computer Vision, Azure OpenAI (GPT-4, DALL-E)
- **Cloud**: Azure App Service, Application Insights
- **Testing**: xUnit, Moq, Integration Tests
- **DevOps**: GitHub Actions, Docker, Azure CLI
- **Monitoring**: Serilog, Application Insights, Custom Metrics

## Azure Services Used

- Azure Computer Vision
- Azure OpenAI Service
- Azure App Service
- Azure Application Insights

## CI/CD Pipeline

The project uses GitHub Actions for continuous integration and deployment:

1. **Build and Test**: Builds the solution and runs tests
2. **Deploy to Azure**: Deploys the Server application to Azure App Service
3. **Docker Build**: Builds and pushes Docker images to GitHub Container Registry
4. **Security Scan**: Performs security scanning and vulnerability checks

To deploy the application to your Azure resources, see the [GitHub Actions Deployment Guide](./docs/github-actions-deployment-guide.md) for setup instructions.

## 🚀 Getting Started

### Prerequisites
- **.NET 9.0 SDK** or later
- **Node.js** and **npm** (for client-side packages)
- **Azure Subscription** with the following services:
  - Azure Computer Vision
  - Azure OpenAI Service
- **Git** for version control

### 🏃‍♂️ Quick Start (Local Development)

1. **Clone the Repository**
   ```bash
   git clone https://github.com/punkouter25/PoRedoImage.git
   cd PoRedoImage
   ```

2. **Install Dependencies**
   ```bash
   dotnet restore
   ```

3. **Configure Azure Services**
   - Copy `appsettings.Example.json` to `appsettings.json`
   - Update with your Azure service credentials:
     ```json
     {
       "ComputerVision": {
         "Endpoint": "https://your-cv-service.cognitiveservices.azure.com/",
         "ApiKey": "your-computer-vision-key",
         "ApiVersion": "2024-02-01"
       },
       "OpenAI": {
         "Endpoint": "https://your-openai-service.openai.azure.com/",
         "ApiKey": "your-openai-key",
         "ChatCompletionsDeployment": "gpt-4",
         "ImageGenerationDeployment": "dall-e-3"
       }
     }
     ```

4. **Build and Run**
   ```bash
   # Build the solution
   dotnet build
   
   # Run the server (starts both API and serves the client)
   cd Server
   dotnet run
   ```

5. **Access the Application**
   - Open your browser to `https://localhost:5001`
   - Upload an image and experience the AI analysis!

### 🐳 Docker Deployment

```bash
# Build the Docker image
docker build -t imagegc:latest .

# Run the container
docker run -p 5000:80 -e ComputerVision__ApiKey=your-key imagegc:latest
```

### ☁️ Azure Deployment

For automated Azure deployment using GitHub Actions:

1. **Set up Azure Resources**
   ```bash
   # Use the provided workflow
   gh workflow run azure-environment-setup.yml
   ```

2. **Configure GitHub Secrets**
   ```bash
   # Run the setup script
   .\setup-github-secrets.ps1
   ```

3. **Deploy**
   ```bash
   # Push to main branch triggers automatic deployment
   git push origin main
   ```

*For detailed deployment instructions, see the [GitHub Actions Deployment Guide](./docs/github-actions-deployment-guide.md).*

## 🧪 Testing

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run specific test categories
dotnet test --filter Category=Integration
```

## 📊 Monitoring & Diagnostics

- **Application Insights**: Performance monitoring and telemetry
- **Health Checks**: Built-in endpoints for service monitoring  
- **Structured Logging**: Serilog with Azure integration
- **Custom Metrics**: Processing times, token usage, error rates
- **Diagnostics Page**: Real-time system status and connectivity tests

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
