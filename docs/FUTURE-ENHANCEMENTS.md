# Future Enhancements Roadmap

This document outlines optional architectural improvements that align with the comprehensive standards defined in the requirements.

## 🎯 Phase 1: Project Structure Standardization (Not Started)

### Objective
Reorganize repository to follow standard .NET project structure with clear separation of concerns.

### Changes Required

#### Current Structure
```
/PoRedoImage/
├── Client/
├── Server/
├── ImageGc.Shared/
├── ImageGc.Tests/
├── Diagrams/
├── docs/
├── e2e-tests/
├── infra/
└── scripts/
```

#### Target Structure
```
/PoRedoImage/
├── /src/
│   ├── PoRedoImage.Api/          # Renamed from Server
│   ├── PoRedoImage.Client/        # Renamed from Client
│   └── PoRedoImage.Shared/        # Renamed from ImageGc.Shared
├── /tests/
│   ├── PoRedoImage.Api.Tests/     # Unit & integration
│   ├── PoRedoImage.Client.Tests/  # bUnit component tests
│   └── PoRedoImage.E2E.Tests/     # Playwright E2E tests
├── /docs/
│   ├── adr/
│   ├── coverage/
│   ├── kql/
│   ├── SECRETS.md
│   └── MIGRATION-SUMMARY.md
├── /infra/
│   └── (existing Bicep files)
├── /scripts/
│   └── (existing PowerShell scripts)
├── global.json
├── Directory.Packages.props
├── PoRedoImage.sln
└── README.md
```

### Implementation Steps
1. Rename solution file: `ImageGc.sln` → `PoRedoImage.sln`
2. Create `/src` folder, move and rename:
   - `Server/` → `/src/PoRedoImage.Api/`
   - `Client/` → `/src/PoRedoImage.Client/`
   - `ImageGc.Shared/` → `/src/PoRedoImage.Shared/`
3. Create `/tests` folder, move and rename:
   - `ImageGc.Tests/` → `/tests/PoRedoImage.Api.Tests/`
   - `e2e-tests/` → `/tests/PoRedoImage.E2E.Tests/`
4. Update all project references in .csproj files
5. Update namespace declarations in all .cs files
6. Update paths in:
   - `.vscode/launch.json`
   - `.vscode/tasks.json`
   - GitHub Actions workflows
   - `azure.yaml`
   - Bicep deployment scripts

### Files to Update
- All `.csproj` files (ProjectReference paths)
- All `.cs` files (namespace declarations)
- `PoRedoImage.sln` (project paths)
- `.vscode/launch.json`
- `.vscode/tasks.json`
- `.github/workflows/*.yml`
- `azure.yaml`
- `infra/app/web.bicep`

### Validation
- [ ] `dotnet build` succeeds
- [ ] `dotnet test` runs all tests
- [ ] F5 debugging works in VS Code
- [ ] `azd deploy` succeeds
- [ ] GitHub Actions CI/CD pipeline works

---

## 🏗️ Phase 2: Vertical Slice Architecture (Not Started)

### Objective
Reorganize API code by feature instead of technical layer.

### Current Structure
```
/Server/
├── Controllers/
│   └── ApiController.cs
├── Services/
│   ├── ComputerVisionService.cs
│   ├── OpenAIService.cs
│   └── HealthChecks/
└── Program.cs
```

### Target Structure
```
/src/PoRedoImage.Api/
├── Features/
│   ├── ImageAnalysis/
│   │   ├── AnalyzeImage.cs          # Endpoint + Handler
│   │   ├── AnalyzeImageValidator.cs  # FluentValidation
│   │   └── ImageAnalysisModels.cs    # Request/Response DTOs
│   ├── Health/
│   │   ├── GetHealth.cs
│   │   └── GetDiagnostics.cs
│   └── Logging/
│       └── LogClientMessage.cs
├── Infrastructure/
│   ├── ComputerVision/
│   │   ├── ComputerVisionClient.cs
│   │   └── ComputerVisionHealthCheck.cs
│   ├── OpenAI/
│   │   ├── OpenAIClient.cs
│   │   └── OpenAIHealthCheck.cs
│   └── Telemetry/
│       └── PoRedoImageMetrics.cs      # OpenTelemetry Meter
└── Program.cs
```

### Implementation Pattern

Each feature slice contains everything needed for that feature:

```csharp
// Features/ImageAnalysis/AnalyzeImage.cs
public static class AnalyzeImage
{
    public record Request(IFormFile Image, int DescriptionLength);
    public record Response(string Description, List<string> Tags, ProcessingMetrics Metrics);
    
    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Image).NotNull();
            RuleFor(x => x.DescriptionLength).InclusiveBetween(200, 500);
        }
    }
    
    public class Handler
    {
        private readonly IComputerVisionClient _visionClient;
        private readonly IOpenAIClient _openAIClient;
        private readonly Meter _meter;
        
        public async Task<Response> Handle(Request request, CancellationToken ct)
        {
            // Implementation using SOLID principles
            // Track metrics with OpenTelemetry
        }
    }
    
    public static void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/analyze", async (Request request, Handler handler) =>
        {
            var result = await handler.Handle(request, CancellationToken.None);
            return Results.Ok(result);
        })
        .WithName("AnalyzeImage")
        .WithOpenApi();
    }
}
```

### Benefits
- ✅ Feature isolation - everything for a feature in one place
- ✅ Easy to test - clear dependencies per feature
- ✅ Easy to navigate - features grouped logically
- ✅ Easy to extend - add new features without touching existing code

---

## 🚨 Phase 3: RFC 7807 Problem Details (Not Started)

### Objective
Return standardized error responses for all non-success HTTP status codes.

### Implementation

#### 1. Create Problem Details Factory
```csharp
// Infrastructure/ProblemDetails/ProblemDetailsFactory.cs
public static class ProblemDetailsFactory
{
    public static IResult ValidationError(string detail, Dictionary<string, string[]> errors)
    {
        return Results.ValidationProblem(
            errors,
            detail: detail,
            title: "One or more validation errors occurred.",
            type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            statusCode: StatusCodes.Status400BadRequest);
    }
    
    public static IResult NotFound(string resourceType, string resourceId)
    {
        return Results.Problem(
            detail: $"{resourceType} with ID '{resourceId}' was not found.",
            title: "Resource not found",
            type: "https://tools.ietf.org/html/rfc7231#section-6.5.4",
            statusCode: StatusCodes.Status404NotFound);
    }
    
    public static IResult ServiceUnavailable(string serviceName, Exception ex)
    {
        return Results.Problem(
            detail: $"The {serviceName} service is currently unavailable: {ex.Message}",
            title: "Service unavailable",
            type: "https://tools.ietf.org/html/rfc7231#section-6.6.4",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
```

#### 2. Update Exception Handler Middleware
```csharp
// Program.cs
app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        
        var problemDetails = exception switch
        {
            ValidationException validationEx => new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Validation Error",
                Detail = validationEx.Message,
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1"
            },
            KeyNotFoundException notFoundEx => new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Resource Not Found",
                Detail = notFoundEx.Message,
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.4"
            },
            _ => new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An error occurred",
                Detail = exception?.Message,
                Type = "https://tools.ietf.org/html/rfc7231#section-6.6.1"
            }
        };
        
        context.Response.StatusCode = problemDetails.Status ?? 500;
        await context.Response.WriteAsJsonAsync(problemDetails);
    });
});
```

#### 3. Update Health Checks
```csharp
// Features/Health/GetHealth.cs
app.MapHealthChecks("/api/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        if (report.Status == HealthStatus.Unhealthy)
        {
            var problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Health check failed",
                Detail = string.Join(", ", report.Entries
                    .Where(e => e.Value.Status == HealthStatus.Unhealthy)
                    .Select(e => $"{e.Key}: {e.Value.Description}")),
                Type = "https://tools.ietf.org/html/rfc7231#section-6.6.4"
            };
            
            context.Response.StatusCode = 503;
            await context.Response.WriteAsJsonAsync(problemDetails);
        }
        else
        {
            await context.Response.WriteAsJsonAsync(new { status = report.Status.ToString() });
        }
    }
});
```

---

## 🔐 Phase 4: OIDC Federated Credentials (Not Started)

### Objective
Use workload identity federation for GitHub Actions instead of publish profiles.

### Implementation Steps

#### 1. Create Azure AD App Registration
```bash
# Create app registration
az ad app create --display-name "PoRedoImage-GitHub-OIDC"

# Create service principal
az ad sp create --id <app-id>

# Add federated credentials
az ad app federated-credential create \
  --id <app-id> \
  --parameters '{
    "name": "PoRedoImage-GitHub",
    "issuer": "https://token.actions.githubusercontent.com",
    "subject": "repo:punkouter26/PoRedoImage:ref:refs/heads/main",
    "audiences": ["api://AzureADTokenExchange"]
  }'
```

#### 2. Assign Azure Permissions
```bash
# Get subscription ID
SUBSCRIPTION_ID=$(az account show --query id -o tsv)

# Assign Contributor role
az role assignment create \
  --assignee <service-principal-id> \
  --role Contributor \
  --scope /subscriptions/$SUBSCRIPTION_ID/resourceGroups/PoRedoImage
```

#### 3. Update GitHub Secrets
```
AZURE_CLIENT_ID: <app-id>
AZURE_TENANT_ID: <tenant-id>
AZURE_SUBSCRIPTION_ID: <subscription-id>
```

#### 4. Update GitHub Actions Workflow
```yaml
# .github/workflows/deploy.yml
name: Deploy to Azure

on:
  push:
    branches: [main]

permissions:
  id-token: write
  contents: read

jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      
      - name: Azure Login (OIDC)
        uses: azure/login@v1
        with:
          client-id: ${{ secrets.AZURE_CLIENT_ID }}
          tenant-id: ${{ secrets.AZURE_TENANT_ID }}
          subscription-id: ${{ secrets.AZURE_SUBSCRIPTION_ID }}
      
      - name: Deploy with azd
        run: azd deploy --no-prompt
        env:
          AZURE_ENV_NAME: production
```

---

## 📊 Phase 5: Enhanced Metrics (Not Started)

### Objective
Implement business-specific OpenTelemetry metrics for detailed observability.

### Implementation

#### 1. Create Metrics Class
```csharp
// Infrastructure/Telemetry/PoRedoImageMetrics.cs
public class PoRedoImageMetrics
{
    private readonly Meter _meter;
    private readonly Counter<long> _imageProcessingCounter;
    private readonly Counter<long> _tokensUsedCounter;
    private readonly Histogram<double> _processingDurationHistogram;
    private readonly Counter<long> _errorCounter;
    
    public PoRedoImageMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create("PoRedoImage.Api");
        
        _imageProcessingCounter = _meter.CreateCounter<long>(
            "poredoimage.processing.count",
            description: "Number of images processed");
        
        _tokensUsedCounter = _meter.CreateCounter<long>(
            "poredoimage.openai.tokens.total",
            description: "Total OpenAI tokens consumed");
        
        _processingDurationHistogram = _meter.CreateHistogram<double>(
            "poredoimage.processing.duration",
            unit: "s",
            description: "Image processing duration");
        
        _errorCounter = _meter.CreateCounter<long>(
            "poredoimage.processing.failures",
            description: "Number of processing failures");
    }
    
    public void RecordImageProcessed(bool success, double durationSeconds)
    {
        _imageProcessingCounter.Add(1, new KeyValuePair<string, object?>("success", success));
        _processingDurationHistogram.Record(durationSeconds);
    }
    
    public void RecordTokensUsed(int promptTokens, int completionTokens, string model)
    {
        _tokensUsedCounter.Add(promptTokens + completionTokens, 
            new KeyValuePair<string, object?>("model", model),
            new KeyValuePair<string, object?>("type", "total"));
    }
    
    public void RecordError(string errorType)
    {
        _errorCounter.Add(1, new KeyValuePair<string, object?>("error_type", errorType));
    }
}
```

#### 2. Register in DI
```csharp
// Program.cs
builder.Services.AddSingleton<PoRedoImageMetrics>();
```

#### 3. Use in Handlers
```csharp
// Features/ImageAnalysis/AnalyzeImage.Handler.cs
public class Handler
{
    private readonly PoRedoImageMetrics _metrics;
    
    public async Task<Response> Handle(Request request, CancellationToken ct)
    {
        var startTime = Stopwatch.GetTimestamp();
        try
        {
            var result = await ProcessImage(request, ct);
            
            var duration = Stopwatch.GetElapsedTime(startTime).TotalSeconds;
            _metrics.RecordImageProcessed(success: true, duration);
            _metrics.RecordTokensUsed(result.PromptTokens, result.CompletionTokens, "gpt-4o");
            
            return result;
        }
        catch (Exception ex)
        {
            _metrics.RecordError(ex.GetType().Name);
            throw;
        }
    }
}
```

---

## ✅ Implementation Checklist

### Phase 1: Project Structure
- [ ] Rename solution file
- [ ] Create /src and /tests folders
- [ ] Move and rename projects
- [ ] Update all project references
- [ ] Update namespaces
- [ ] Update build/deployment configs
- [ ] Verify build and tests

### Phase 2: Vertical Slice Architecture
- [ ] Create Features/ folder structure
- [ ] Implement first feature slice (ImageAnalysis)
- [ ] Add FluentValidation to Shared project
- [ ] Migrate existing endpoints
- [ ] Update tests
- [ ] Document pattern in ADR

### Phase 3: Problem Details
- [ ] Create ProblemDetailsFactory
- [ ] Update exception handler middleware
- [ ] Update all endpoint error responses
- [ ] Update health checks
- [ ] Test error scenarios

### Phase 4: OIDC Federation
- [ ] Create Azure AD app registration
- [ ] Configure federated credentials
- [ ] Update GitHub secrets
- [ ] Update workflow files
- [ ] Test deployment
- [ ] Remove publish profile secrets

### Phase 5: Enhanced Metrics
- [ ] Create PoRedoImageMetrics class
- [ ] Register in DI
- [ ] Instrument image analysis
- [ ] Instrument AI service calls
- [ ] Create KQL queries for new metrics
- [ ] Update monitoring documentation

---

## 📚 References

- [Vertical Slice Architecture](https://www.jimmybogard.com/vertical-slice-architecture/)
- [RFC 7807 Problem Details](https://www.rfc-editor.org/rfc/rfc7807.html)
- [OIDC with GitHub Actions](https://docs.github.com/en/actions/deployment/security-hardening-your-deployments/configuring-openid-connect-in-azure)
- [OpenTelemetry Metrics](https://opentelemetry.io/docs/specs/otel/metrics/api/)
- [FluentValidation](https://docs.fluentvalidation.net/)

---

**Status:** Optional enhancements - implement as needed based on project requirements.
