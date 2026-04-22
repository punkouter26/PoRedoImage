---
description: "Use when working on PoRedoImage.Infrastructure"
applyTo: "src/PoRedoImage.Infrastructure/**"
---

---
description: "Use when working on PoRedoImage.Infrastructure"
applyTo: "src/PoRedoImage.Infrastructure/**"
---

# PoRedoImage.Infrastructure — Area Instructions

## Role
Concrete implementations of all Domain interfaces (`PoRedoImage.Domain.Interfaces`) and Application service interfaces (`PoRedoImage.Application.Services`). This is the only project that references Azure SDKs, ImageSharp, and external HTTP APIs. All registrations are exposed via a single extension method consumed by the Web host.

For domain interface contracts see [poredoimage-domain.instructions.md](poredoimage-domain.instructions.md).  
For orchestration patterns see [poredoimage-application.instructions.md](poredoimage-application.instructions.md).

## Directory Layout
```
Repositories/
  AzureBlobUserImageRepository.cs   # IUserImageRepository — Blob (bytes) + Table (metadata)
  AzureTableBulkPromptRepository.cs # IBulkPromptRepository — Table Storage
Services/
  AzureVisionService.cs             # IVisionService — Azure Computer Vision
  AzureOpenAiService.cs             # IGenerativeAiService — Azure OpenAI (chat + image)
  GeminiImagen3Service.cs           # IImagen3Service — Gemini REST API
  ImageSharpMemeGeneratorService.cs # IMemeGeneratorService — SixLabors.ImageSharp
InfrastructureServiceExtensions.cs  # Single DI registration entry point
```

## DI Registration
All services and repositories are wired in `AddPoRedoImageInfrastructure()` — **never** register infrastructure types directly in `Program.cs`.

| Service | Lifetime | Reason |
|---|---|---|
| `AzureVisionService` | Singleton | `ImageAnalysisClient` owns long-lived HTTP resources |
| `AzureOpenAiService` | Singleton | `AzureOpenAIClient` is thread-safe and expensive to construct |
| `GeminiImagen3Service` | Singleton | `IHttpClientFactory` consumer; stateless |
| `ImageSharpMemeGeneratorService` | Scoped | Stateless; scoped is fine |
| `AzureBlobUserImageRepository` | Singleton | `BlobContainerClient` + `TableClient` are thread-safe |
| `AzureTableBulkPromptRepository` | Singleton | `TableClient` is thread-safe |
| `UserImageService` | Scoped | Application-layer; depends on singleton repos |
| `ImageAnalysisOrchestrator` | Scoped | Application-layer orchestrator |

## Graceful Degradation Pattern
All services that depend on external credentials follow the same pattern:
1. In the constructor, check required config keys; if absent, store a `_configurationError` string and log a warning — **do not throw**.
2. In every public method, `if (_configurationError is not null) throw new InvalidOperationException(_configurationError)` before doing any work.
3. For `IImagen3Service`, expose `IsConfigured` as `!string.IsNullOrWhiteSpace(_configuration["Google:ApiKey"])` — callers must check this before invoking generation.
4. For repositories, when `Storage:ConnectionString` is absent, return empty collections / no-ops rather than throwing.

## Secret Rotation (Singleton Lifetime)
Singletons hold `AzureKeyCredential` instances that can be updated in-place. Re-read secrets from `IConfiguration` on every public call:
```csharp
var currentKey = _configuration["ComputerVision:ApiKey"];
if (!string.IsNullOrWhiteSpace(currentKey) && _credential is not null)
    _credential.Update(currentKey);
```
This allows Key Vault secret rotation to take effect without restarting the process. Apply the same pattern for any singleton holding an `AzureKeyCredential`.

## Lazy Storage Initialization
Repositories use a `SemaphoreSlim(1,1)` + bool `_initialized` double-check pattern to call `CreateIfNotExistsAsync` exactly once per process lifetime:
```csharp
private async Task EnsureInitializedAsync(CancellationToken ct)
{
    if (_initialized) return;
    await _initLock.WaitAsync(ct);
    try { if (!_initialized) { /* CreateIfNotExists */ _initialized = true; } }
    finally { _initLock.Release(); }
}
```
Call `EnsureInitializedAsync` at the top of every public repository method.

## Azure Storage Conventions
- **Blob container**: `"user-images"`, access type `None` (no public access). Blob names follow `"{userId}/{imageId}"`.
- **Tables**: `"UserImages"` (PartitionKey = userId, RowKey = imageId) and `"BulkPrompts"` (PartitionKey always `"prompts"`).
- Each repository declares a private `internal sealed` `ITableEntity` class (e.g., `UserImageTableEntity`) co-located in the same file. These are never exposed outside the repository.
- Table entity → domain entity mapping is done via a `private static MapToDomain(...)` method inside each repository.
- `UpsertEntityAsync` (not `AddEntityAsync`) is used for both insert and update to remain idempotent.

## HttpClient — Gemini
`GeminiImagen3Service` uses the named client `"GeminiApi"` registered with `AddStandardResilienceHandler`:
- Total timeout: 5 minutes (Imagen generation is slow).
- Max retries: 2.
- Re-read `Google:ApiKey` from `IConfiguration` on every request (no credential caching) by setting the `x-goog-api-key` header per-request.
- Model routing: if `Google:Imagen3Model` starts with `"gemini-"` → use `generateContent` (multimodal) endpoint; otherwise → use Vertex AI `predict` endpoint.

## OpenAI Client Construction
`AzureOpenAiService` supports split endpoints (`OpenAI:Endpoint` for chat, `OpenAI:ImageEndpoint` for image generation):
- If both endpoints are identical, share one `AzureOpenAIClient` and one credential.
- If endpoints differ, build separate clients with `BuildClientWithCredential(endpoint, apiKey)`.
- When `apiKey` is null/empty → use `DefaultAzureCredential` (managed identity); otherwise → `AzureKeyCredential`.

## ImageSharp Meme Rendering
`ImageSharpMemeGeneratorService` renders bold white text with a black stroke, centered horizontally:
- Font priority fallback: Impact → Liberation Sans → DejaVu Sans → Arial → Helvetica → first available system font.
- Font size is auto-fit: start at `imageHeight / 8`, step down by 2 until text fits within `imageWidth - padding * 2`.
- Top text baseline: `padding` from top; bottom text baseline: `imageHeight * 0.65f` from top.
- Output is always PNG (`PngEncoder`), content type `"image/png"`.

## Configuration Keys
| Key | Used By |
|---|---|
| `ComputerVision:Endpoint` | `AzureVisionService` |
| `ComputerVision:ApiKey` (or `ComputerVision:Key`) | `AzureVisionService` |
| `ComputerVision:MinTagConfidence` | `AzureVisionService` (default `0.6`) |
| `OpenAI:Endpoint` | `AzureOpenAiService` |
| `OpenAI:ImageEndpoint` | `AzureOpenAiService` (defaults to `OpenAI:Endpoint`) |
| `OpenAI:ChatCompletionsDeployment` | `AzureOpenAiService` (default `"gpt-4o"`) |
| `OpenAI:ImageGenerationDeployment` | `AzureOpenAiService` (default `"dall-e-3"`) |
| `OpenAI:Key` / `OpenAI:ImageKey` | `AzureOpenAiService` (absent → `DefaultAzureCredential`) |
| `Google:ApiKey` | `GeminiImagen3Service` |
| `Google:Imagen3Model` | `GeminiImagen3Service` (default `"gemini-2.0-flash-exp-image-generation"`) |
| `Storage:ConnectionString` | Both repositories |

## Adapter Pattern
Every implementation is an **Adapter** (GoF): it translates the Azure/Google SDK surface into the clean domain interface. Implementations must not leak SDK types through their public signatures — all public methods use primitive types, `byte[]`, or domain entities defined in `PoRedoImage.Domain`.

## Class Conventions
- All implementations are `sealed` classes using **traditional constructor DI** (not primary constructors, unlike Application layer).
- Namespace root: `PoRedoImage.Infrastructure.Services` / `PoRedoImage.Infrastructure.Repositories`.
- `TreatWarningsAsErrors` is enabled globally — resolve all nullable warnings before committing.
- Use `Stopwatch.GetTimestamp()` / `Stopwatch.GetElapsedTime(start).TotalMilliseconds` for elapsed-time measurement (not `DateTime.UtcNow`).
- `ArgumentNullException.ThrowIfNull` / `ArgumentException.ThrowIfNullOrWhiteSpace` for input guards at the top of public methods (after the config-error check).
