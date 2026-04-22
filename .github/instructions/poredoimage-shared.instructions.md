---
description: "Use when working on PoRedoImage.Shared"
applyTo: "src/PoRedoImage.Shared/**"
---

---
description: "Use when working on PoRedoImage.Shared"
applyTo: "src/PoRedoImage.Shared/**"
---

# PoRedoImage.Shared — Area Instructions

## Role
Contract library shared between the Blazor WASM client (`PoRedoImage.Client`) and the ASP.NET Core host (`PoRedoImage.Web`). Contains only DTOs — no business logic, no services, no infrastructure concerns.

References `PoRedoImage.Domain` solely to reuse the `UserImageKind` enum; no domain entities are exposed through DTOs.

## Directory Layout
```
DTOs/
  ImageAnalysisRequest.cs   # Client → API: image bytes + pipeline config
  ImageAnalysisResponse.cs  # API → Client: analysis results + metrics
  ProcessingMode.cs         # Enum: ImageRegeneration | MemeGeneration
  UserImageDtos.cs          # Gallery CRUD request/response records
  BulkGenerateDtos.cs       # Bulk-generate request/response records + status enum
```

## DTO Type Conventions
| Shape | When to use |
|---|---|
| `record` (positional) | Simple request/response pairs with value semantics and no optional fields |
| `class` with property initializers | Complex DTOs with many fields, optional nullables, or computed properties |
| `enum` | Discriminated mode/status values (`ProcessingMode`, `BulkGenerateStatus`) |

## Patterns
- **`ImageData` fields** always carry Base64-encoded image bytes as `string`; **`ContentType`** fields always carry a MIME type string (e.g. `"image/png"`).
- **DataAnnotations** (`[Required]`, `[Range]`) are placed on *request* DTOs only — they are enforced by `ValidationFilter<T>` in the Web layer (see [poredoimage-web.instructions.md](poredoimage-web.instructions.md)). Response DTOs carry no annotations.
- **Nullable `string?`** for truly optional response fields; non-nullable `string` with `= string.Empty` default for fields that must always be present.
- `ProcessingMetricsDto.TotalProcessingTimeMs` is a **computed property** (sum of the three sub-timings) — never add a setter.
- Namespace root: `PoRedoImage.Shared.DTOs`.

## Dependencies
- Zero NuGet packages — only `System.ComponentModel.DataAnnotations` (inbox) and a project reference to `PoRedoImage.Domain`.
- `Nullable` and `ImplicitUsings` are enabled; `TreatWarningsAsErrors` is set globally — resolve all nullable warnings before committing.

## What Does NOT Belong Here
- Business logic, validation rules beyond DataAnnotations, or orchestration → `PoRedoImage.Application`
- Domain entities or repository interfaces → `PoRedoImage.Domain`
- Endpoint routing or HTTP handling → `PoRedoImage.Web`
- Infrastructure / Azure SDK types → `PoRedoImage.Infrastructure`
