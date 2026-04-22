---
description: "Use when working on PoRedoImage.Domain"
applyTo: "src/PoRedoImage.Domain/**"
---

---
description: "Use when working on PoRedoImage.Domain"
applyTo: "src/PoRedoImage.Domain/**"
---

# PoRedoImage.Domain — Area Instructions

## Role
Pure domain layer — no framework dependencies, no infrastructure concerns. Contains domain entities and service/repository interfaces consumed by `PoRedoImage.Application` and `PoRedoImage.Infrastructure`. This project has **zero NuGet dependencies** beyond the .NET 10 SDK.

## Directory Layout
```
Entities/
  UserImage.cs         # Gallery image entity (uploaded + AI-processed)
  UserImageKind.cs     # Enum: Original | Regeneration | Meme | BulkVariation
  BulkPrompt.cs        # Bulk-generate prompt entity (Table Storage row)
Interfaces/
  IUserImageRepository.cs    # Blob bytes + metadata CRUD
  IBulkPromptRepository.cs   # BulkPrompt CRUD
  IVisionService.cs          # Image analysis (Computer Vision)
  IGenerativeAiService.cs    # Text/image generation (OpenAI)
  IImagen3Service.cs         # Image generation via Imagen 3 (Google)
  IMemeGeneratorService.cs   # Meme overlay generation (ImageSharp)
```

## Entities
- All entities are **immutable** (`init`-only properties) with `sealed` classes.
- Use the static `Create(...)` factory method to construct entities — never use object initializers directly from outside the entity.
- `Id` on `UserImage` defaults to `Guid.NewGuid().ToString("N")` (no hyphens); `RowKey` on `BulkPrompt` is a standard `Guid.ToString()`.
- `CreatedAt` always uses `DateTimeOffset.UtcNow` — never `DateTime`.
- `BulkPrompt.PartitionKey` is always the literal `"prompts"`.

## Interfaces
- **ISP applies**: each interface covers exactly one capability — don't merge concerns.
- Return types use value tuples (named) for multi-value returns (e.g., `(byte[] ImageData, string ContentType, long ElapsedMs)`).
- All async methods accept a `CancellationToken ct = default` as the last parameter.
- Nullable return signals "not found / access denied" (e.g., `Task<UserImage?>`, `Task<(byte[], string)?>`).
- `IImagen3Service` exposes an `IsConfigured` bool property — check it before calling generation methods when the Google credentials may be absent.
- Repository interfaces combine blob and metadata concerns where they belong to the same aggregate (`IUserImageRepository`).

## Conventions
- Namespace root: `PoRedoImage.Domain.Entities` / `PoRedoImage.Domain.Interfaces`.
- No business logic in this project — validation, orchestration, and mapping live in `PoRedoImage.Application`.
- No logging, no DI attributes — interfaces are plain C# contracts.
- `TreatWarningsAsErrors` is enabled globally (see `Directory.Build.props`); all nullable warnings must be resolved.
- Implementations live in `PoRedoImage.Infrastructure`; orchestration lives in `PoRedoImage.Application`. The Domain project must never reference either.
