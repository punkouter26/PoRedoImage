---
description: "Use when working on PoRedoImage.Tests.Unit"
applyTo: "tests/PoRedoImage.Tests.Unit/**"
---

---
description: "Use when working on PoRedoImage.Tests.Unit"
applyTo: "tests/PoRedoImage.Tests.Unit/**"
---

# PoRedoImage.Tests.Unit — Area Instructions

## Role
Pure unit tests with **zero** external calls (no HTTP, no Azure SDK, no Google API). Only guard clauses, constructor validation, pure logic, and DTO defaults are covered here. Integration-style tests (full HTTP pipeline, real storage) belong in `PoRedoImage.Tests.Integration` — see [poredoimage-tests-integration.instructions.md](poredoimage-tests-integration.instructions.md).

## Directory Layout
```
Features/   # Tests for infrastructure services and Web feature logic
Models/     # Tests for Shared DTO defaults and computed properties
```

## Tech Stack
- **xUnit** — globally imported via `<Using Include="Xunit" />`; no per-file `using Xunit;` needed
- **Moq** — mock `ILogger<T>` and `IHttpClientFactory`; loggers are sinks only (never `.Verify(...)`)
- **Azure.Security.KeyVault.Secrets** — `SecretModelFactory` for Key Vault unit tests only
- **SixLabors.ImageSharp** — generate minimal in-memory PNG bytes; no external test asset files
- **Microsoft.Extensions.Configuration** — `ConfigurationBuilder().AddInMemoryCollection(dict).Build()` for all config setup

## Patterns & Conventions

### Test naming
`MethodName_Condition_ExpectedResult` — e.g., `AnalyzeAsync_EmptyData_ThrowsArgument`.

### Structure within a test class
Group related tests under ASCII section banners:
```csharp
// ─── Guard clauses ──────────────────────────────────────────────
// ─── Constructor tests ──────────────────────────────────────────
// ─── Output correctness ─────────────────────────────────────────
```

### AAA layout
Use `// Arrange`, `// Act`, `// Assert` comments in non-trivial tests; single-line tests may collapse Act and Assert.

### Service instantiation
Instantiate infrastructure services directly (not via DI). Build config with a private `BuildConfig(...)` helper that accepts nullable overrides:
```csharp
private static IConfiguration BuildConfig(string? endpoint = "https://test.example.com/", string? key = "test-key")
{
    var dict = new Dictionary<string, string?>();
    if (endpoint != null) dict["Section:Key"] = endpoint;
    return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
}
```

### Graceful degradation tests
Missing config must **not throw at construction** — verify with `Assert.NotNull(service)`.  
Missing config **must throw at call time** — verify with `Assert.ThrowsAsync<InvalidOperationException>(...)`.  
(See infrastructure graceful degradation rules in [poredoimage-infrastructure.instructions.md](poredoimage-infrastructure.instructions.md).)

### Minimal PNG generation
When tests need real image bytes, generate them programmatically — never ship binary test assets:
```csharp
private static byte[] CreateMinimalPng()
{
    using var image = new Image<Rgba32>(1, 1);
    using var ms = new MemoryStream();
    image.Save(ms, new PngEncoder());
    return ms.ToArray();
}
```

### Cost control
XML doc comments on service test classes must state that no API calls are made and token usage is zero. Do not write tests that trigger real HTTP requests.

### `[Theory]` + `[InlineData]`
Use for exhaustive coverage of multiple known-good or known-bad values (e.g., all Key Vault secret names, all invalid input variants).

### No `IClassFixture<T>`
Each test class constructs its own instances per test; there is no shared expensive state to fixture.

## Run Command
```bash
dotnet test tests/PoRedoImage.Tests.Unit
```
