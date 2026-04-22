---
description: "Use when working on PoRedoImage.Tests.Integration"
applyTo: "tests/PoRedoImage.Tests.Integration/**"
---

---
description: "Use when working on PoRedoImage.Tests.Integration"
applyTo: "tests/PoRedoImage.Tests.Integration/**"
---

# PoRedoImage.Tests.Integration — Area Instructions

## Role
Black-box integration tests for the ASP.NET Core Web host. All tests exercise real HTTP endpoints via `WebApplicationFactory<Program>`. No unit-test mocking of routing or middleware — the full pipeline runs.

For endpoint contracts and domain interfaces, see the existing instruction files in `.github/instructions/`.

## Tech Stack
- **xUnit** — test runner; `IClassFixture<T>` for shared factory lifetime
- **Microsoft.AspNetCore.Mvc.Testing** — `WebApplicationFactory<Program>`
- **Moq** — mock domain interfaces (`IVisionService`, `IGenerativeAiService`, etc.)
- **Testcontainers** — Docker-based Azurite (`mcr.microsoft.com/azure-storage/azurite:latest`) for real Table Storage round-trip tests

## Factory Hierarchy
| Factory | Purpose |
|---|---|
| `CustomWebApplicationFactory` | Default — no external calls; storage disabled; auth overridden |
| `MockedServicesWebApplicationFactory` | Mocks all domain AI/vision services; zero API cost |
| `ThrowingComputerVisionWebApplicationFactory` | `IVisionService` throws `HttpRequestException`; tests 500 error paths |

All factories:
1. Force `Development` environment (skips Key Vault, uses dev cookie auth fallback).
2. Zero out `AZURE_KEY_VAULT_ENDPOINT`, `Storage:ConnectionString`, `ApplicationInsights:ConnectionString`, and `Google:ApiKey` via `AddInMemoryCollection` to prevent real outbound calls in CI.

## Auth Override Pattern
`TestAuthHandler` (`SchemeName = "Test"`) auto-authenticates every request as:
- `UserId = "test-user-integration-001"`, `UserEmail = "test@example.com"`

Register via `PostConfigure<AuthenticationOptions>` to ensure it overrides the dev cookie scheme set in `Program.cs`.

## Service Replacement Pattern
Use `ReplaceService<T>` (in `MockedServicesWebApplicationFactory`) to swap domain interface registrations:
```csharp
private static void ReplaceService<T>(IServiceCollection services, T mockInstance) where T : class
{
    var descriptors = services.Where(d => d.ServiceType == typeof(T)).ToList();
    foreach (var d in descriptors) services.Remove(d);
    services.AddScoped(_ => mockInstance);
}
```
Remove **all** existing descriptors for the interface before adding the mock — singletons and scoped may both exist.

## Docker / Testcontainers Tests
- Tag Docker-dependent tests with `[Trait("Category", "Docker")]`.
- Skip by default with `[Fact(Skip = "Requires Docker daemon; ...")]` so CI passes without Docker.
- Use random host port (`WithPortBinding(0, 10002)`) for Table Storage to avoid port conflicts.
- Implement `IAsyncLifetime` for container start/stop lifecycle.
- Azurite well-known dev connection string: `AccountName=devstoreaccount1` with the fixed well-known account key.

## Image Magic Bytes in Tests
Use real magic-byte prefixes when constructing test payloads:
- PNG: `{ 0x89, 0x50, 0x4E, 0x47 }`
- JPEG: `{ 0xFF, 0xD8, 0xFF, 0xE0 }`

The API rejects images that fail magic-byte validation with HTTP 400.

## Run Command
```bash
dotnet test tests/PoRedoImage.Tests.Integration
# Skip Docker tests:
dotnet test tests/PoRedoImage.Tests.Integration --filter "Category!=Docker"
```
