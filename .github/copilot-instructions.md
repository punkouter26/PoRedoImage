1. Foundation
Solution Naming: The .sln file name (e.g., Po*****) is the base identifier. It must be used as the name for all Azure services/resource groups (e.g., Po*****), and the user-facing HTML <title>.
.NET Version: All projects must target .NET 10. The global.json file must be locked to a 10.0.xxx SDK version. Use latest C# features.
Package Management: All NuGet packages must be managed centrally in a Directory.Packages.props file at the repository root.
Null Safety: Nullable Reference Types (<Nullable>enable</Nullable>) must be enabled in all .csproj files.
2. Architecture
Code Organization: The API must use Vertical Slice Architecture. All API logic (endpoints, CQRS handlers) must be co-located by feature in /src/Po.[AppName].Api/Features/.
Design Philosophy: Apply SOLID principles and standard GoF design patterns. Document their use in code comments or the PRD.
API Design: Use Minimal APIs for all new endpoints / The API project must host the Blazor WASM project
Repository Structure: Adhere to the standard root folder structure: /src, /tests, /docs, /infra, and /scripts.
/src projects must follow the separation of concerns: ...Api, ...Client, and ...Shared.
The ...Shared project must only contain DTOs, contracts, and shared validation logic (e.g., FluentValidation rules) that are referenced by both the ...Api and ...Client projects. It must not contain any business logic or data access code.
/docs will contain the README.md(Describe app and how to run it), mermaid diagrams, KQL query library, and ADRs.
/scripts contains helper scripts that the coding LLM creates.
3. Implementation
API & Backend
API Documentation: All API endpoints must have Swagger (OpenAPI) generation enabled. .http files must be maintained for manual verification.
Health Checks: Implement a health check endpoint at api/health that validates connectivity to all external dependencies.
Error Handling: All non-successful API responses (4xx, 5xx) must return an IResult that serializes to an RFC 7807 Problem Details JSON object. Use structured ILogger.LogError within all catch blocks.
Frontend (Blazor)
UI Framework Principle: Standard Blazor WASM controls and the primary component library. Radzen.Blazor may only be used for complex requirements as needed .
Responsive Design: The UI must be mobile-first (portrait mode), responsive, fluid, and touch-friendly.
Development Environment
Debug Launch: The environment must support a one-step 'F5' debug launch for the API and browser. (Implementation: Commit a launch.json with a serverReadyAction to the repository).
Keys: All keys must be stored in  appSetting until app is deployed to Azure. The Program.cs file must be configured to read from Azure Key Vault only when the ASPNETCORE_ENVIRONMENT is 'Production'. After app is deployed both local and Azure code should refer to keys in Azure Key Vault with the exception of local code using Azurite instead of Azure Storage
Local Storage: Use Azurite for local development and integration testing).
4. Quality & Testing
Code Hygiene: All build warnings/errors must be resolved before pushing changes to GitHub. Run dotnet format to ensure style consistency.
Dependency Hygiene: Regularly check for and apply updates to all packages via Directory.Packages.props.
Workflow: Apply a TDD workflow (Red -> Green -> Refactor) for all business logic (e.g., MediatR handlers, domain services). For UI and E2E tests, tests must be written contemporaneously with the feature code.
Test Naming: Test methods must follow the MethodName_StateUnderTest_ExpectedBehavior convention.
Code Coverage (dotnet-coverage):
Enforce a minimum 80% line coverage threshold for all new business logic.
A combined coverage report must be generated in docs/coverage/.
Unit Tests (xUnit): Must cover all backend business logic (e.g., MediatR handlers) with all external dependencies mocked.
Component Tests (bUnit): Must cover all new Blazor components (rendering, user interactions, state changes), mocking dependencies like IHttpClientFactory.
Integration Tests (xUnit): A "happy path" test must be created for every new API endpoint, running against a test host and an in-memory database emulator. Realistic test data should be generated.
E2E Tests (Playwright):
Start API before running E2E tests.
Tests must target Chromium (mobile and desktop views).
Full-Stack E2E (Default): Runs the entire stack (frontend + API + test database) to validate a true user flow.
Isolated E2E (By Exception): Uses network mocking only for specific scenarios that are difficult to set up (e.g., simulating a 3rd-party payment provider failure).
Integrate automated accessibility and visual regression checks.
5. Operations & Azure
Provisioning: All Azure infrastructure must be provisioned using Bicep (from the /infra folder) and deployed via Azure Developer CLI (azd).
CI/CD: The GitHub Actions workflow must use Federated Credentials (OIDC) for secure, secret-less connection to Azure.
GitHub CI/CD: The YML file must simply build the code and deploy it to the resource group (e.g., PoProject-rg) as an App Service (e.g., PoProject-app).
Required Services: Bicep scripts must provision, at minimum: Application Insights & Log Analytics, App Service, and Azure Storage to all be in the same resource group.
Cost Management: A $5 monthly cost budget must be created for the application's resource group. The budget must be configured with an Action Group to send an email alert to the project owner punkouter26@gmail.com when 80% of the threshold is met.
Logging:
Use Serilog for all structured logging.
Configuration must be driven by appsettings.json to write to the Debug Console (in Development) and Application Insights (in Production).
Telemetry:
Use modern OpenTelemetry abstractions for all custom telemetry.
Metrics: Use Meter to create custom metrics for business-critical values.
Production Diagnostics:
Enable the Application Insights Snapshot Debugger on the App Service.
Enable the Application Insights Profiler on the App Service.
KQL Library: The docs/kql/ folder must be populated with essential queries for monitoring app specific parameters, users, actions performed etc.
