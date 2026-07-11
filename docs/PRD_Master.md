---
project: PoRedoImage
tier: 1
type: prd
last_updated: 2026-07-11
dotnet: "10.0"
csharp: "14"
---

# PRD Master — PoRedoImage

> **Source of Truth** · API contracts · Vertical Slice definitions · .NET 10 constraints

---

## 1. Product Vision

PoRedoImage is a cloud-native AI image studio that transforms ordinary photos into artistic masterpieces, memes, and stylistic variations in seconds. Chain Azure Computer Vision, Azure OpenAI GPT-4.1-nano, and Google Gemini Imagen3 behind a Blazor Web App for zero-prompt-engineering AI image manipulation.

**Core Promise:** Upload → Choose Style → Gallery-Ready Result in < 10 s.

---

## 2. Vertical Slice Definitions

| Slice | Route Prefix | Purpose | Auth Required | Anonymous Paths |
|-------|--------------|---------|---------------|-----------------|
| **Auth** | `/auth/*` | OIDC + dev cookie login (`/auth/login/microsoft`, `/auth/login/fake`, `/auth/logout`, `/auth/me`) | n/a (entry) | yes — `/auth/login/*`, `/auth/logout` |
| **ImageAnalysis** | `/api/images` | CV → OpenAI → Gemini pipeline | Yes (FallbackPolicy) | — |
| **BulkGenerate** | `/api/bulk-generate` | 10× parallel Gemini variations | Yes | — |
| **StyleDirector** | `/api/style-director` | 4-agent sequential workflow | Yes | — |
| **MemeTemplates** | `/api/meme-templates` | Reusable meme layout library | Yes | — |
| **UserImages** | `/api/user-images` | Per-user gallery CRUD | Yes | — |
| **Pricing** | `/api/pricing` | Active image-gen provider + indicative prices | Yes | — |
| **Diagnostics** | `/api/diag` | Masked config + health checks | Yes | — |
| **Host shell** | `/`, `/scalar/v1`, `/health`, `/alive`, static | App shell + docs | n/a | yes — host shell + health endpoints |

> **Rule:** every endpoint under `/api` is fail-closed (`FallbackPolicy = RequireAuthenticatedUser`). The WASM `[Authorize]` attribute is **UI-only** and never the security boundary.

---

## 3. API Contracts (selected)

### 3.1 ImageAnalysis

```yaml
POST /api/images/analyze
Request:
  ImageAnalysisRequest:
    ImageData: string (base64, required)
    ContentType: string (required, e.g. "image/jpeg")
    FileName: string (optional)
    DescriptionLength: int (200–500, default 200)
    Mode: ProcessingMode (ImageRegeneration=0 | MemeGeneration=1)
Response:
  ImageAnalysisResponse:
    Description: string
    Tags: string[]
    ConfidenceScore: double
    RegeneratedImageData: string? (base64)
    RegeneratedImageContentType: string (default "image/png")
    Metrics: ProcessingMetricsDto
    MemeImageData: string? (base64)
    MemeCaption: string?
Errors:
  400: Invalid image / FluentValidation failure
  401: Unauthorized (no auth cookie)
  422: AI content policy / Gemini declined
  429: Rate limited (>10 req/min/IP)
  503: AI key/endpoint mismatch
```

### 3.2 BulkGenerate

```yaml
GET  /api/bulk-generate/prompts           # load saved art prompts (auth)
POST /api/bulk-generate/prompts           # save art prompts    (auth)
POST /api/bulk-generate/describe          # GPT-4o vision caption (auth)
POST /api/bulk-generate/variation         # single variation    (auth)
POST /api/bulk-generate/reroll            # re-roll N variations from winning prompt (auth)
```

### 3.3 UserImages

```yaml
POST /api/user-images/original            # save original upload bytes
POST /api/user-images/result              # save generated result bytes
GET  /api/user-images                     # list user's saved images
```

### 3.4 Auth

```yaml
GET  /auth/login/microsoft?returnUrl=…   # Microsoft Entra OIDC challenge (canonical)
GET  /auth/login/fake?email=…            # Dev/Test sign-in (canonical; GUEST or named)
GET  /auth/logout                         # sign out (canonical)
GET  /auth/me?returnUrl=…                 # server auth state + returnUrl validation (401 if anon)
```

---

## 4. Data Contracts (DTOs)

```csharp
public sealed record ImageAnalysisRequest(
    string ImageData,
    string ContentType,
    string? FileName = null,
    int DescriptionLength = 200,
    ProcessingMode Mode = ProcessingMode.ImageRegeneration);

public sealed record ImageAnalysisResponse(
    string Description,
    IReadOnlyList<string> Tags,
    double ConfidenceScore,
    string? RegeneratedImageData,
    string RegeneratedImageContentType,
    ProcessingMetricsDto Metrics,
    string? MemeImageData,
    string? MemeCaption);

public sealed record ProcessingMetricsDto(
    long ImageAnalysisTimeMs,
    long DescriptionGenerationTimeMs,
    long ImageRegenerationTimeMs,
    int DescriptionTokensUsed,
    string? ErrorInfo,
    long TotalProcessingTimeMs);

public enum ProcessingMode : byte { ImageRegeneration = 0, MemeGeneration = 1 }
```

DTOs are declared in `PoRedoImage.Shared/DTOs/` and **shared between WASM and the API**. They must be:

- **trim-safe** — no reflection on closed generics, no `dynamic`, no `BinaryFormatter`
- **explicit AOT annotations** — `[JsonSerializable(typeof(...))]` source-generated
- **readonly records** — prefer `sealed record` over mutable classes
- **zero-alloc logging** — `LoggerMessage.Define` source generators, never `$"..."` interpolation in hot paths

---

## 5. .NET 10 / C# 14 Constraints (must hold)

| Concern | Rule |
|---|---|
| `<Nullable>` | `enable` (global) |
| `<TreatWarningsAsErrors>` | `true` |
| `<EnableTrimAnalyzer>` | `true` on `Shared`, `Client`, `Web` |
| `<ImplicitUsings>` | `enable` per project |
| AOT | **disabled** (interpreted WASM); no AOT-only APIs without annotations |
| Source-gen logging | **required** in production code paths |
| Central Package Management | yes — `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>` |
| Versioning | MinVer via Git tags |

---

## 6. Trimmer-Compatible Model Criteria

A type is "trim-safe" iff:

1. It is a `sealed record` (or `sealed class` with explicit `[DynamicallyAccessedMembers]`).
2. All properties are blittable, public, and have either:
   - primitive types (`string`, `int`, `double`, `DateTimeOffset`, `Guid`, `byte[]`)
   - other trim-safe types
   - `IReadOnlyList<T>` / `IReadOnlyDictionary<K,V>` of trim-safe T
3. JSON serialization uses **`System.Text.Json` source generators** (`[JsonSerializable(typeof(T))]`).
4. No `dynamic`, no `MethodInfo.Invoke`, no `Activator.CreateInstance(t)` without an AOT `MetadataLoadContext` annotation.
5. No reflection on closed generic instantiations (`typeof(List<>).MakeGenericType(...)` is forbidden).

---

## 7. Zero-Allocation Source-Generated Logging Standards

```csharp
// ✔ Correct — source-generated, zero-alloc
private static readonly ILogger<ImageAnalysisHandler> _log =
    LoggerMessage.Define<string, int>(
        LogLevel.Information,
        new EventId(1, "AnalyzeStart"),
        "Image analysis started. ContentType={ContentType}, Bytes={Length}");

// ✗ Forbidden in hot paths
_log.LogInformation($"Analyzing {contentType} ({bytes.Length} bytes)");
```

Rules:

- Every log call site uses `LoggerMessage.Define` or `LoggerMessage.DefineScope`.
- `[EventId]` ranges are reserved per feature slice (Auth: 1–99, ImageAnalysis: 100–199, BulkGenerate: 200–299, etc.).
- Verbose/Trace levels must guard via `if (_log.IsEnabled(...))` even when using source-gen.
- `IDisposable` scopes use `BeginScope` with `LogContext.PushProperty` for Serilog cross-cut.

---

## 8. Feature → Endpoint Map

| Feature slice (server) | HTTP path(s) | DTO file |
|---|---|---|
| `Features/Auth/`          | `/auth/*`, `/api/diag/mock-status`             | `Features/Auth/AuthDtos.cs` |
| `Features/ImageAnalysis/` | `POST /api/images/analyze`                      | `Shared/DTOs/ImageAnalysis*.cs` |
| `Features/BulkGenerate/`  | `POST /api/bulk-generate/*`, `GET/POST /api/bulk-generate/prompts` | `Shared/DTOs/Bulk*.cs` |
| `Features/MemeTemplates/` | `GET /api/meme-templates`, `POST /api/meme-templates/render` | `Shared/DTOs/Meme*.cs` |
| `Features/StyleDirector/` | `POST /api/style-director/run`                  | `Shared/DTOs/StyleDirector*.cs` |
| `Features/UserImages/`    | `GET/POST /api/user-images*`                     | `Shared/DTOs/UserImage*.cs` |
| `Features/Pricing/`       | `GET /api/pricing`                              | `Shared/DTOs/Pricing*.cs` |
| `Features/Diagnostics/`   | `GET /api/diag`                                 | `Shared/DTOs/Diag*.cs` |
| `Features/Idempotency/`   | middleware filter for `POST /api/images/analyze` (cross-slice) | — |

---

## 9. Telemetry Surface

| Signal | Type | Source |
|---|---|---|
| `poredoimage.request.duration` | histogram (ms) | ASP.NET Core Activity, tagged `http.route`, `http.status_code` |
| `poredoimage.images.analyze.total_ms` | histogram | `ProcessingMetricsDto.TotalProcessingTimeMs` |
| `poredoimage.images.analyze.tokens` | histogram | `ProcessingMetricsDto.DescriptionTokensUsed` |
| `poredoimage.bulk.variations.count` | counter | per-call N |
| `poredoimage.auth.failures` | counter | OIDC failure events |
| `poredoimage.up{component="..."}` | gauge | health-check results |

All spans carry: `correlation_id`, `session_id`, `user_id_hash`, `feature_slice`, `route`.

---

## 10. Versioning

- Library version: **MinVer** (driven by `git tag`, e.g. `0.4.1`).
- API version: **route-prefix versioning** (`/api/v1/...` reserved for future; today all routes are v0).
- DTO compatibility: additive-only. Breaking changes require a new namespace `PoRedoImage.Shared.V2` and explicit OptIn at the consumer.