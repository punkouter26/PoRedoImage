# PoRedoImage — Architecture Record

> **System Prompt for LLMs:** This document is the authoritative source of truth for the PoRedoImage solution. Read it fully before suggesting any changes. All coding decisions must align with these conventions.

---

## 1. Solution Identity

| Property | Value |
|---|---|
| Solution name | `PoRedoImage` |
| Master prefix | `PoRedoImage` (all namespaces, Azure resources, Key Vault secrets) |
| .NET version | **10.0** (pinned in `global.json`) |
| C# version | **14** (`<LangVersion>latest</LangVersion>`) |
| Assembly versioning | **MinVer** — driven by `git tag` (e.g., `v1.2.3`). Never edit version strings manually. |

---

## 2. Architecture: Onion / Clean

```
PoRedoImage.Domain          ← Core entities & interfaces (no external deps)
PoRedoImage.Application     ← Use-case orchestrators, DTOs shared with server
PoRedoImage.Infrastructure  ← Azure SDK adapters, repo implementations
PoRedoImage.Shared          ← DTOs shared between WASM client & server
PoRedoImage.Web             ← ASP.NET Core host: Blazor SSR + API endpoints
PoRedoImage.Client          ← Blazor WASM interactive components
```

Dependency rule: inner rings must never reference outer rings. Domain ← Application ← Infrastructure ← Web.

---

## 3. Key Conventions

### Naming
- Namespaces follow project name: `PoRedoImage.Domain.Entities`, `PoRedoImage.Infrastructure.Services`, etc.
- Azure resources: prefix `poredoimage-` (e.g., `poredoimage-app`, `kv-poshared`)
- Key Vault secrets: `PoRedoImage-{Section}-{Key}` (e.g., `PoRedoImage-OpenAI-ApiKey`)

### Patterns (document with `<remarks>`)
- Adapter (GoF): Infrastructure services adapt Azure SDKs to Domain interfaces
- Extension Method: `AddPoRedoImageInfrastructure()`, `AddPoRedoImageAuth()` for DI registration
- Vertical Slice: Each feature folder (`Features/Auth`, `Features/ImageAnalysis`, etc.) owns its endpoints, models, and UI

### SOLID Tagging
Use `<remarks>` XML tags to call out which SOLID/GoF principle a type applies, so LLMs understand intent:
```csharp
/// <remarks>
/// Adapter pattern (GoF): adapts AzureSDK to IVisionService so Domain stays SDK-free.
/// </remarks>
```

---

## 4. Authentication

- **Dev (no ClientId):** Cookie-only. Login page shows both Microsoft and ANON buttons.
- **Prod:** Microsoft OIDC (Entra ID) + cookie.
- **ANON identity:** suffix `ANON{6-digit-random}`, created on `/dev-login?email=anon@anon.local`. Displays as "ANON LOGGED IN" in the nav bar.
- Managed Identity used for all Azure service authentication in Production (Azure App Service).

---

## 5. Local Development

| Concern | Approach |
|---|---|
| Ports | HTTP: 5000, HTTPS: 5001 |
| Storage | Azurite via Docker Compose (`docker-compose.azurite.yml`) |
| Secrets | `appsettings.Development.json` (empty placeholders) + Key Vault (`kv-poshared`) |
| F5 | `preLaunchTask: kill-dotnet-and-build` kills stale processes before launch |
| Setup | `SCRIPTS/setup.ps1` — one-command machine setup (Winget, Docker, Azurite, mock keys) |

---

## 6. Observability

- **Serilog:** Console + File + Application Insights. Enriched with `UserId`, `SessionId`, `CorrelationId`.
- **OpenTelemetry:** Traces & metrics exported to Azure Monitor (Application Insights) when connection string is set.
- **RequestContextMiddleware:** Injects correlation ID header and pushes user/session context into Serilog LogContext.
- **Dev transparency:** Stack traces surfaced in UI (`Error.razor` shows exception details in Development).

---

## 7. Mock Data Visibility

Any service implementing `IMockable` (defined in `PoRedoImage.Domain.Interfaces`) causes the nav bar to display a **"USING MOCK DATA"** badge. This ensures LLMs and developers can immediately spot mock-mode operation.

Registration pattern:
```csharp
services.AddSingleton<IVisionService, MockVisionService>(); // MockVisionService : IVisionService, IMockable
```

---

## 8. Tech Debt & Known Limitations

- `LangVersion` is set to `latest`; review after each .NET SDK update to confirm C# 14 feature set.
- MinVer requires at least one git tag (`v0.0.0`) to produce a clean version; bootstrap with `git tag v0.0.0`.
- Azurite Docker container must be running before the app starts locally (`SCRIPTS/setup.ps1` handles this).
- `PoRedoImage.Client.csproj` does not inherit `TreatWarningsAsErrors` from `Directory.Build.props` because the WASM SDK build chain currently has upstream warnings; this is tracked and will be re-enabled when resolved.

---

## 9. Zero-Waste Policy

Delete unused files, dead code, and obsolete assets immediately. No commented-out code blocks. When removing a feature, also remove its DI registration, endpoints, and UI.
