---
project: PoRedoImage
tier: 1
type: prd
last_updated: 2026-06-01
dotnet: "10.0"
csharp: "14"
stack:
  - Blazor Web App (SSR + Interactive Server + WASM)
  - Azure Container Apps
  - Azure Table Storage
  - Azure Blob Storage
  - Azure Key Vault
  - Azure Computer Vision
  - Azure OpenAI GPT-4.1-nano
  - Google Gemini gemini-2.5-flash-image
---

# PRD Master — PoRedoImage

> **Source of Truth** · API contracts · Vertical Slice definitions · .NET 10 constraints

---

## 1. Product Vision

PoRedoImage is a cloud-native AI image studio that transforms ordinary photos into artistic masterpieces, memes, and stylistic variations in seconds. Chain Azure Computer Vision, Azure OpenAI GPT-4.1-nano, and Google Gemini Imagen3 behind a Blazor Web App for zero-prompt-engineering AI image manipulation.

**Core Promise:** Upload → Choose Style → Gallery-Ready Result in < 10s.

---

## 2. Vertical Slice Definitions

| Slice | Route Prefix | Purpose | Auth Required |
|-------|-------------|---------|---------------|
| **Auth** | `/dev-login`, `/challenge-microsoft`, `/logout` | OIDC + dev cookie login | No |
| **ImageAnalysis** | `/api/images` | CV → OpenAI → Gemini pipeline | No (rate-limited) |
| **BulkGenerate** | `/api/bulk-generate` | 10× parallel Gemini variations | Prompt CRUD: Yes / AI: No |
| **CaptionBattle** | `/api/caption-battle` | 8-persona parallel caption gen | No (rate-limited) |
| **MemeTemplates** | `/api/meme-templates` | Reusable meme layout library | No |
| **StyleDirector** | `/api/style-director` | 4-agent sequential workflow | No (rate-limited) |
| **UserImages** | `/api/user-images` | Per-user gallery CRUD | Yes |
| **Diagnostics** | `/api/diag` | Masked config + health checks | Yes |

---

## 3. API Contracts

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
  ProcessingMetricsDto:
    ImageAnalysisTimeMs: long
    DescriptionGenerationTimeMs: long
    ImageRegenerationTimeMs: long
    DescriptionTokensUsed: int
    ErrorInfo: string?
    TotalProcessingTimeMs: long (computed)
Errors:
  400: Invalid image / validation failure
  401: Unauthorized (no auth cookie)
  422: Content policy violation / generation declined
  429: Rate limit exceeded (10 req/min per user)
  503: AI service authentication failure
  500: Processing error
```

### 3.2 BulkGenerate

```yaml
GET /api/bulk-generate/prompts
Auth: Required
Response: string[] (exactly 10 prompts)

POST /api/bulk-generate/prompts
Auth: Required
Request:
  SavePromptsRequest:
    Prompts: string[] (exactly 10, each ≤2000 chars)
Response: 204 No Content

POST /api/bulk-generate/describe
Auth: Not required (rate-limited)
Request:
  BulkDescribeRequest:
    ImageData: string (base64)
    ContentType: string
Response:
  BulkDescribeResponse:
    Description: string

POST /api/bulk-generate/variation
Auth: Not required (rate-limited)
Request:
  BulkVariationRequest:
    ImageData: string (base64)
    ContentType: string
    Prompt: string
Response:
  BulkVariationResponse:
    ImageData: string (base64)
    ContentType: string

POST /api/bulk-generate/reroll
Auth: Not required (rate-limited)
Request:
  BulkRerollRequest:
    ImageData: string (base64)
    SeedPrompt: string
    Count: int (1–10)
Response:
  BulkRerollResponse:
    Variations: BulkRerollVariation[]
    Requested: int
    Succeeded: int
    ElapsedMs: long
```

### 3.3 Diagnostics

```yaml
GET /api/diag
Auth: Required
Response:
  Environment: string
  MachineName: string
  OSVersion: string
  DotNetVersion: string
  ProcessId: int
  Timestamp: string (ISO 8601)
  Health:
    Status: string
    TotalDurationMs: double
    Entries: dict[string, {Status, Description, DurationMs}]
  Configuration:
    (all values masked: "sk-a***3456" pattern)
```

### 3.4 Health

```yaml
GET /health
Response:
  Status: string ("Healthy" | "Degraded" | "Unhealthy")
  Duration: double (ms)
  Entries: array of { Key, Status, Duration, Description }
Checks: key-vault, computer-vision, openai, table-storage, imagen3

GET /alive
Response: 200 OK (liveness probe, no dependency checks)
```

---

## 4. Domain Entities

### 4.1 UserImage

```csharp
public sealed class UserImage {
    string UserId        // PartitionKey — user identity
    string Id            // RowKey — GUID "N"
    string FileName
    string ContentType   // default "image/jpeg"
    UserImageKind Kind   // Original | Regeneration | Meme | BulkVariation
    DateTimeOffset CreatedAt
    long SizeBytes
}
```

### 4.2 BulkPrompt

```csharp
public sealed class BulkPrompt {
    string PartitionKey   // "prompts"
    string RowKey         // userId
    string Name
    string PromptText     // JSON-serialized string[]
    DateTimeOffset CreatedAt
}
```

### 4.3 MemeTemplate

```csharp
public sealed record MemeTemplate(
    string Id,                    // kebab-case stable ID
    string Name,                  // display name
    string Description,
    string Category,              // classic | reaction | office | wholesome | experimental
    int RequiredZoneCount,
    IReadOnlyList<MemeTextZone> Zones
);

public sealed record MemeTextZone(
    string Label,
    double X, double Y,           // normalized 0..1
    double MaxWidthRatio,
    double FontSizeRatio,
    string Alignment              // center | left | right
);
```

### 4.4 Enums

```csharp
enum UserImageKind  { Original, Regeneration, Meme, BulkVariation }
enum ProcessingMode { ImageRegeneration = 0, MemeGeneration = 1 }
enum BulkGenerateStatus { Pending, Processing, Complete, Failed }
enum CaptionPersona { GenZ, Corporate, Absurdist, DadJoke, Sarcastic, Wholesome, TechBro, Surreal }
enum StorageError   { NotConfigured, NotFound, TransientFailure, Conflict, CircuitOpen, Unknown }
```

---

## 5. .NET 10 Specific Constraints

| Constraint | Rule | Rationale |
|-----------|------|-----------|
| **No reflection for AOT** | Avoid `Activator.CreateInstance`, runtime type inspection | .NET 10 Native AOT compatibility |
| **Minimal APIs only** | No MVC controllers; use `MapGroup` + static handler methods | Vertical Slice Architecture |
| **Nullable + Warnings as errors** | Enforced via `Directory.Build.props` | Zero-defect culture |
| **Options Pattern with Validation** | `IValidateOptions<T>` + `ValidateOnStart()` | Fail-fast on missing config |
| **Result<T,E> pattern** | Discriminated union for service operations | Replaces silent null returns |
| **Idempotency via IEndpointFilter** | `[Idempotent]` marker attribute + `IMemoryCache` | Prevent duplicate writes |
| **Rate Limiting** | Sliding window: 10 req/min per user/IP | Protect costly AI calls |
| **Request Body Size** | 25 MB max (Kestrel + FormOptions) | Prevent OOM on ACA pods |
| **Magic-byte validation** | JPEG, PNG, GIF, WebP, BMP; HEIC hint | Po2Logic F10 |
| **Key Vault rotation** | 30 min `ReloadInterval` with `KeyVaultSecretNameMapping` | Zero-downtime secret rotation |
| **Serilog structured logging** | Console (Dev) + File + Application Insights (Prod) | Observability |
| **OpenTelemetry → Azure Monitor** | Traces + Metrics export via `UseAzureMonitor()` | No OTLP collector needed |

---

## 6. Key Metrics

| Metric | Target |
|--------|--------|
| E2E image regeneration latency | < 10s p95 |
| Bulk generate (10 variations) wall-clock | < 45s p95 |
| CI test coverage gate | ≥ 80% (opencover) |
| Production deployment success rate | ≥ 99% (OIDC zero-secret deploy) |
| `/health` uptime SLA | 99.5% |

---

## 7. Non-Goals (v1)

- Native mobile app (responsive web only)
- Video processing
- Real-time collaborative editing
- Custom model fine-tuning UI