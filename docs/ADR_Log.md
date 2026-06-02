---
project: PoRedoImage
tier: 3
type: registry
last_updated: 2026-06-01
format: "ADR (Architecture Decision Record)"
---

# ADR Log — PoRedoImage

> Why certain technologies or patterns were chosen. Bulleted for zero-waste AI token efficiency.

---

## ADR-001: Blazor Web App (SSR + Interactive Server + WASM)

- **Decision:** Use .NET 10 Blazor Web App with hybrid SSR + Interactive Server + WASM rendering.
- **Why:** Single language (C#) across full stack. SSR for fast initial load, WASM for rich interactivity, SignalR for real-time sync.
- **Alternatives:** React SPA + .NET API (rejected: two languages, two build systems).
- **Trade-off:** Larger initial bundle; mitigated by SSR pre-rendering.

## ADR-002: Minimal APIs (No MVC Controllers)

- **Decision:** All endpoints use `MapGroup` + static handler methods. No `Controller` classes.
- **Why:** Vertical Slice Architecture — each feature owns its endpoint + logic co-located. Reduces ceremony, improves token efficiency.
- **Alternatives:** MVC controllers with `[ApiController]` (rejected: ceremony, routing overhead).
- **Trade-off:** Less "standard" for .NET devs familiar with MVC; mitigated by consistent pattern.

## ADR-003: Azure Computer Vision + OpenAI + Gemini (Multi-AI)

- **Decision:** Chain Azure CV (vision) → Azure OpenAI GPT-4.1-nano (language) → Google Gemini Imagen3 (generation).
- **Why:** Each AI service excels at its domain. CV for tagging, OpenAI for description enhancement, Gemini for image generation.
- **Alternatives:** Single-provider (e.g., all OpenAI): rejected due to Gemini's superior image-to-image quality.
- **Trade-off:** Three API keys, three health checks; mitigated by Key Vault + health check endpoints.

## ADR-004: Azure Table Storage + Blob Storage (Dual Storage)

- **Decision:** Table Storage for entities (UserImage metadata, BulkPrompts). Blob Storage for raw image bytes.
- **Why:** Table Storage is cheap, fast for metadata queries. Blob Storage handles large binary efficiently. Separation keeps costs low.
- **Alternatives:** Cosmos DB (rejected: overkill for current scale, 3× cost).
- **Trade-off:** Two storage backends to manage; mitigated by repository pattern.

## ADR-005: Key Vault with 30-Minute Reload

- **Decision:** Load secrets from Azure Key Vault with `ReloadInterval = 30 min` and `KeyVaultSecretNameMapping`.
- **Why:** Zero-downtime secret rotation. No app restart needed when keys rotate.
- **Alternatives:** App Settings only (rejected: no rotation, manual restart required).
- **Trade-off:** 30-min window where stale secrets could cause failures; mitigated by health checks.

## ADR-006: Result<T,E> Discriminated Union

- **Decision:** Use `Result<T, E>` struct instead of null returns or exceptions for expected failures.
- **Why:** Eliminates silent no-ops (Po2Logic Failure #9). Forces callers to handle both success and error paths.
- **Alternatives:** Nullable returns (rejected: hidden failures), exceptions (rejected: expensive for expected cases).
- **Trade-off:** More verbose call sites; mitigated by `Match()` pattern.

## ADR-007: Idempotency via IEndpointFilter

- **Decision:** `[Idempotent]` marker attribute + `IdempotencyKeyFilter` backed by `IMemoryCache` with 24h TTL.
- **Why:** Prevents duplicate writes from network retries (Po2Logic F6). 24h TTL prevents replays across days.
- **Alternatives:** Client-side dedup (rejected: unreliable), database constraints only (rejected: late detection).
- **Trade-off:** Memory pressure from cached keys; mitigated by TTL eviction.

## ADR-008: Agentic Style Director (4-Agent Sequential)

- **Decision:** Implement Idea #1 as a 4-agent sequential workflow: VisionAnalyst → StyleStrategist → PromptRefiner → Critic.
- **Why:** Explainable AI — each agent produces a reasoning entry visible in the UI. Separation of concerns.
- **Alternatives:** Single monolithic prompt (rejected: no explainability), parallel agents (rejected: sequential dependency).
- **Trade-off:** Higher latency (4 sequential calls); mitigated by streaming progress to UI.

## ADR-009: SkiaSharp for Meme Overlay (Not ImageSharp)

- **Decision:** Use SkiaSharp for meme text overlay, ImageSharp for meme generation service.
- **Why:** SkiaSharp provides fine-grained text layout control (font size, alignment, wrapping). ImageSharp is used for the broader meme template system.
- **Alternatives:** HTML canvas overlay (rejected: server-side only), FFmpeg (rejected: overkill for text overlay).
- **Trade-off:** Two image libraries; justified by different use cases (text layout vs. template management).

## ADR-010: OIDC with Zero-Secret Deploy

- **Decision:** Production uses Microsoft Entra ID OIDC with `ResponseType = "code"`. Dev uses cookie-only auth.
- **Why:** Zero-secret deployment — OIDC code flow doesn't require client secrets in production. Dev bypass for rapid iteration.
- **Alternatives:** Client credentials flow (rejected: requires secret management), no auth (rejected: security).
- **Trade-off:** Dev/prod auth divergence; mitigated by `AuthServiceExtensions` conditional registration.

## ADR-011: Radzen Blazor Component Library

- **Decision:** Use Radzen Blazor components for UI (Button, Card, Tabs, Upload, ProgressBar, etc.).
- **Why:** Mature Blazor component library with dark theme support. Reduces custom CSS. Bento Box layout compatible.
- **Alternatives:** MudBlazor (rejected: heavier customization overhead), Syncfusion (rejected: licensing cost).
- **Trade-off:** Vendor dependency; mitigated by Radzen's open-source core.

## ADR-012: Serilog + OpenTelemetry → Application Insights

- **Decision:** Structured logging via Serilog (Console + File + App Insights). Traces/metrics via OpenTelemetry → Azure Monitor.
- **Why:** Dual telemetry: Serilog for structured logs, OTel for distributed traces. No OTLP collector needed.
- **Alternatives:** NLog only (rejected: no OTel integration), pure OTel (rejected: no structured logging).
- **Trade-off:** Two telemetry pipelines; justified by complementary capabilities.