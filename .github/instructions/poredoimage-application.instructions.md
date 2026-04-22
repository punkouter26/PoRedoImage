---
description: "Use when working on PoRedoImage.Application"
applyTo: "src/PoRedoImage.Application/**"
---

---
description: "Use when working on PoRedoImage.Application"
applyTo: "src/PoRedoImage.Application/**"
---

# PoRedoImage.Application — Area Instructions

## Role
Orchestration layer that composes Domain interfaces (from `PoRedoImage.Domain`) and DTOs (from `PoRedoImage.Shared`) into coherent use-case workflows. No infrastructure concerns, no HTTP primitives. Implementations are registered by `PoRedoImage.Infrastructure`'s DI extension; this project only provides interfaces + orchestrators.

## Directory Layout
```
Services/
  IImageAnalysisOrchestrator.cs   # Pipeline interface (Analyze → Enhance → Generate)
  ImageAnalysisOrchestrator.cs    # Sealed implementation
  IUserImageService.cs            # Gallery CRUD interface
  UserImageService.cs             # Sealed implementation
```

## NuGet Dependencies
- `Microsoft.Extensions.Logging.Abstractions` — `ILogger<T>` injection only
- `Microsoft.Extensions.Http` — available for any future `HttpClient` factory use
- No ORM, no Azure SDK, no AI SDK — those belong in `PoRedoImage.Infrastructure`

## Patterns & Conventions

### Class structure
- Every service is a `sealed` class using **primary-constructor DI** (C# 12).
- Each implementation pairs with its own interface in the same file folder (no sub-namespaces needed).
- Namespace root: `PoRedoImage.Application.Services`.

### Validation & guards
- Use `ArgumentNullException.ThrowIfNull(request)` at the top of public methods that accept reference-type inputs.
- No DataAnnotations validation here — that is enforced at the endpoint layer via `ValidationFilter<T>` (see [poredoimage-web.instructions.md](poredoimage-web.instructions.md)).

### Async & cancellation
- All async methods accept `CancellationToken ct = default` as the last parameter and pass it through to every domain call.

### DTO mapping
- Map `UserImage` → `UserImageDto` inline using LINQ `.Select(...)` — no AutoMapper or third-party mapper.
- Image access URLs follow the pattern `/api/user-images/{id}` (hardcoded string interpolation).
- Return `IReadOnlyList<T>` from gallery queries (`.ToList().AsReadOnly()`).

### Domain entity construction
- Always use the static `UserImage.Create(...)` factory — never object initializers. See [poredoimage-domain.instructions.md](poredoimage-domain.instructions.md).

### Persistence in `UserImageService`
- Save blob first (`SaveBlobAsync`), then metadata (`SaveMetadataAsync`) — two sequential awaits; there is no distributed transaction.

## `ImageAnalysisOrchestrator` Pipeline

```
ProcessAsync(request)
  │
  ├─ visionService.AnalyzeAsync(imageBytes)        ← always runs
  │
  ├─ [MemeGeneration mode]
  │     aiService.GenerateMemeCaptionAsync(tags)
  │     memeService.GenerateMemeAsync(imageBytes, top, bottom)
  │
  └─ [ImageRegeneration mode]
        aiService.EnhanceDescriptionAsync(description, tags, length)
        if imagen3Service.IsConfigured → imagen3Service.GenerateAsync(enhanced)
        else                           → aiService.GenerateImageAsync(enhanced)
```

- Always check `imagen3Service.IsConfigured` before calling Imagen3 — Google credentials may be absent in dev.
- Decode incoming `request.ImageData` from Base64 at the start; encode output image bytes to Base64 in the response.
- Populate `ProcessingMetricsDto` fields from the `ElapsedMs` values returned by each domain service call.

## Logging
- Log pipeline start/end with `LogInformation`, including key identifiers (`Mode`, `TotalMs`, image `Id`, `UserId`).
- Structured logging only — no string concatenation; use message templates.
- No logging of raw image bytes or PII beyond user ID.

## What Does NOT Belong Here
- Infrastructure registrations (DI `AddScoped` / `AddSingleton`) → `PoRedoImage.Infrastructure`
- Endpoint routing, HTTP status codes, `IFormFile` handling → `PoRedoImage.Web`
- Domain entity definitions or repository interfaces → `PoRedoImage.Domain`
- `TreatWarningsAsErrors` is enabled globally — all nullable warnings must be resolved before committing.
