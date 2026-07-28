# PoRedoImage — AI Agent Context

## Overview
PoRedoImage is a Blazor WebAssembly (global Interactive WebAssembly, no prerender) SPA for AI-powered
image analysis, regeneration, meme generation, and bulk generation, hosted behind an ASP.NET Core
Backend-for-Frontend (BFF). The `.Web` project is the API/BFF host only; all interactive UI (router,
layout, pages) lives in and runs from the `.Client` WASM assembly. The server side organises by
**Vertical Slice Architecture (VSA)** — feature folders under `.Web/Features/{Auth, BulkGenerate, ...}`
co-locate endpoint, DTO, and validation per slice — and the Domain / Application / Infrastructure
projects supply cross-cutting primitives (entities, interfaces, repository implementations) consumed
by the slices. **VSA wins over Onion when both would apply** to a feature; Onion layering is
preserved only for cross-slice concerns.

## Quick Facts
- **Solution**: PoRedoImage (.slnx format)
- **Master Prefix**: `PoRedoImage` for namespaces, Azure resources
- **Framework**: .NET 10 (pinned via global.json)
- **Client**: Blazor WASM (global InteractiveWebAssembly, prerender disabled) + Radzen UI — owns router, layout, all pages
- **Server (.Web)**: API/BFF host — minimal-API endpoints, OIDC+cookie auth, serves the WASM client
- **Ports**: HTTP 4000 / HTTPS 4001 (fixed)
- **Database**: Azure Table Storage (Azurite locally)
- **Auth**: Microsoft OIDC (prod) + GUEST mode (dev/test)
- **Telemetry**: OpenTelemetry → Application Insights + Serilog
- **Versioning**: MinVer via Git tags

## Project Structure
```
src/
├── PoRedoImage.Domain/       # Entities, Interfaces (cross-slice primitives)
├── PoRedoImage.Application/  # Service orchestration
├── PoRedoImage.Infrastructure/ # Repositories, external services, IMockable mocks
├── PoRedoImage.Shared/       # DTOs + FluentValidation shared between WASM and API
├── PoRedoImage.Client/       # Blazor WASM SPA — Routes, Layout, Pages, Shared, Models, wwwroot
└── PoRedoImage.Web/          # API/BFF host (App.razor host doc + Features/*/ endpoints)
tests/                          # 4 projects per spec §2.2 — the E2EAPI/E2EUI split is DONE.
├── PoRedoImage.Tests.Unit/        # xUnit + Moq — pure logic
├── PoRedoImage.Tests.Integration/ # xUnit + Testcontainers (Azurite) + WebApplicationFactory
├── PoRedoImage.Tests.E2E.ApiSmoke/ # pure HTTP API smoke (no Playwright dependency)
└── PoRedoImage.Tests.E2E.UI/       # C# Playwright browser tests (Playwright + Chromium install)
```
> Render model: only `.Client` ships to the browser, so every interactive component lives there.
> `.Web` holds just `Components/App.razor` (host document) + `_Imports.razor`; it renders the Client's
> `<Routes>`/`<HeadOutlet>` with `RenderModes.WasmNoPrerender`. Server-side `Features/{Name}/` folders
> own their endpoints, DTOs, validators, and feature-specific services.

## Key Architecture Rules
1. **VSA on the server + WASM/BFF on the client**: `.Web/Features/{Name}/` slices own their endpoints, DTOs, and validators. Domain/Application/Infrastructure supply cross-slice primitives. **VSA wins over Onion when both would apply** — a new feature goes into a new slice, not a new layer in Onion.
2. **No AOT**: Blazor WASM uses interpreted mode for smaller builds and faster CI/CD.
3. **TreatWarningsAsErrors** and **Nullable` enabled globally.
4. **Central Package Management** via `Directory.Packages.props` at root.
5. **Zero-Waste Policy**: Immediately delete unused files/dead code.
6. **BFF invariant**: Blazor WASM must never store or handle access tokens. The server authenticates via `HttpOnly`+`SameSite=Strict` cookies (set in `AuthServiceExtensions.HardenCookie`; `Secure` is `Always` outside Development) and serializes the principal (claims only) to the client via `AddAuthenticationStateSerialization` ↔ `AddAuthenticationStateDeserialization`. The `Microsoft.AspNetCore.Components.WebAssembly.Authentication` package IS referenced on `.Client` (it supplies `AddAuthenticationStateDeserialization`), but only the **safe, claims-only** extension is used — `AddOidcAuthentication()` and any token-handling API in that package are forbidden.
7. **Server-side authz is fail-closed (§4.5)**: `AuthorizationOptions.FallbackPolicy = RequireAuthenticatedUser`. Every endpoint requires auth unless it explicitly `.AllowAnonymous()` — the public set is the WASM host shell, `MapStaticAssets`, `/health`, `/alive`, favicon, OpenAPI/Scalar, all `/auth/*`, and `/api/diag/mock-status`. Client `[Authorize]` is UI-only. **Adding a new anonymous endpoint requires an explicit `.AllowAnonymous()`.**
8. **Request correlation (§6.9)**: the WASM `CorrelationHeaderHandler` stamps `X-Session-ID` (stable per tab) + `X-Correlation-ID` (per request) on every BFF call; server `RequestContextMiddleware` reads, echoes, and pushes them into the Serilog LogContext.

## Auth Modes (BFF)
- Cookie is the default auth + challenge scheme: an unauthenticated page hit redirects to `/login`
  (not straight to Microsoft). The explicit `/auth/*` routes below are the canonical entry points —
  `/auth/login/microsoft` invokes OIDC, `/auth/login/fake` is the dev/test bypass, `/auth/logout` signs
  out (cookie + OIDC), and `/auth/me` returns the server auth state with `returnUrl` validation.
  Legacy `/challenge-microsoft`, `/dev-login`, and `/logout` are still wired as back-compat aliases.
  `/api/*` returns 401.
- The server serializes the cookie principal to the WASM client via `AddAuthenticationStateSerialization`;
  the client reads it via `AddAuthenticationStateDeserialization` — claims only, **no tokens in the browser**.
- **Production**: Microsoft OIDC required. GUEST button hidden/disabled.
- **Development**: Microsoft OIDC + GUEST mode (click "Continue as GUEST").
- **Test env**: `Auth:EnableFakeAuth=true` swaps the entire pipeline for `FakeAuthHandler`
  (header-driven `X-Fake-User`/`X-Fake-Roles`). Defense in depth: the handler also throws
  `InvalidOperationException` if instantiated in Production.
- **GUEST**: Random 8-digit suffix (e.g., GUEST35367543). Persisted via LocalStorage.
- **Dev login**: `/dev-login?email=guest@guest.local` or `/dev-login?guestId=GUEST12345678`
  (legacy aliases preserved for the Login.razor links; canonical is `/auth/login/fake`).
- **Issuer validation**: `ValidateIssuer=false` was replaced with an `IssuerValidator` that accepts
  any `iss` matching `https://login.microsoftonline.com/{tenant}/v2.0`. This preserves the
  multi-tenant + personal-account behavior (`common` authority) while rejecting malformed issuer
  strings a misconfigured `Authority` could otherwise produce.

## API Endpoints
| Endpoint | Description |
|---|---|
| `GET /health` | Health check (JSON) |
| `GET /alive` | Liveness probe |
| `GET /api/diag` | Diagnostics with masked secrets |
| `POST /api/images/analyze` | Image analysis |
| `GET /api/bulk-generate/prompts` | Load saved bulk prompts |
| `POST /api/bulk-generate/prompts` | Save bulk prompts |
| `POST /api/bulk-generate/describe` | GPT-4o vision caption for an image |
| `POST /api/bulk-generate/variation` | Single bulk variation |
| `POST /api/bulk-generate/reroll` | Re-roll N variations from a winning prompt |
| `POST /api/user-images/original` | Save original upload |
| `POST /api/user-images/result` | Save generated result |
| `GET /api/user-images` | List user's saved images |
| `GET /api/meme-templates` | List meme templates |
| `POST /api/meme-templates/render` | Render meme from template |
| `POST /api/style-director/run` | 4-agent style director |
| `POST /api/rap-roast` | Photo → roast lyrics + performed track (Lyria 3) |
| `GET /auth/login/microsoft?returnUrl=…` | Microsoft OIDC challenge (canonical) |
| `GET /auth/login/fake?email=…` | Dev/Test sign-in (canonical; GUEST or named user) |
| `GET /auth/logout` | Sign out (canonical) |
| `GET /auth/me?returnUrl=…` | Server auth state + returnUrl validation (401 when anonymous) |
| `GET /challenge-microsoft` | Legacy alias for /auth/login/microsoft |
| `GET /dev-login?email=…` | Legacy alias for /auth/login/fake (Development only) |
| `GET /logout` | Legacy alias for /auth/logout |
| `GET /scalar/v1` | Interactive API docs |

## Health & Diagnostics
- `/health` — JSON response for automated pings
- `/diag` — Status of connections with masked keys (hidden page, no UI link)
- Named checks: key-vault, computer-vision, openai, table-storage, imagen3

## Storage Strategy
- **Local**: Azurite (Docker) via `UseDevelopmentStorage=true`
- **Azure**: Table Storage with strongly typed DTOs (ITableEntity)
- **Auto-switch**: Logic toggles between Azurite and production Azure Storage

## Secret Management
1. **Primary**: Azure Key Vault (`kv-poshared`, in the `PoShared` resource group)
2. **Backup**: App Service Application Settings (Key Vault references)
3. **Identity**: System-assigned Managed Identity (`Key Vault Secrets User` on `kv-poshared`)
4. **App resources** (App Service `poredoimage-web`, storage) live in the `PoRedoImage` resource group.
   The web app runs on the Basic B1 Linux plan `asp-poredoimage-b1` (was F1 free tier until 2026-06-29 — see ADR-025 for why F1's 60-min/day CPU cap was unsustainable).

## Key Configuration
```json
{
  "KeyVault.Uri": "https://kv-poshared.vault.azure.net/",
  "AzureAd.ClientId": "(set in KV or appsettings)",
  "AzureAd.TenantId": "common",
  "Storage:ConnectionString": "UseDevelopmentStorage=true (local)"
}
```

## UI Conventions
- **Left-aligned top nav bar** with brand + NavMenu + LoginDisplay
- **"USING MOCK DATA" banner** shown when IMockable services active
- **GUEST LOGGED IN** shown in nav bar for guest users
- **Environment badge** in footer for non-production
- **Mobile-first**: CSS clamp() and auto-fit grid

## Testing
- **Layout (spec §2.2)**: four projects — `Tests.Unit`, `Tests.Integration`, `Tests.E2E.ApiSmoke`
  (pure HTTP), `Tests.E2E.UI` (C# Playwright). The E2EAPI/E2EUI split is complete.
- **Unit** (`Tests.Unit`): xUnit + Moq. Pure logic, no I/O.
- **Integration** (`Tests.Integration`): xUnit + `Microsoft.AspNetCore.Mvc.Testing` + Testcontainers
  (Azurite, non-AI mock data). Tests own their own throwaway Azurite container — the
  `docker-compose.yml` Azurite is for the local dev inner loop only (F5) and must never be touched
  by tests (see ADR-019).
- **E2E API smoke** (`Tests.E2E.ApiSmoke`): pure HTTP against a running instance, no browser.
- **E2E UI** (`Tests.E2E.UI`): C# Playwright browser tests; class names containing "Ui" identify the
  UI suite. `[LiveServerFact]` self-skips when no live instance is reachable. Browsers install via the
  generated `playwright.ps1`.
- **Per-tier method ceilings** (100/50/25) are enforced by `TestCountCeilingTests`.
- **CI/CD does NOT run tests** (project policy) — run suites locally or in a dedicated
  non-deploy workflow. `E2E_BASE_URL` overrides the target (default `http://localhost:4000`).
- **Test data**: Non-AI mock data for integration/E2E. Real AI data for local dev.
- **Environment isolation**: integration + E2E tests run with `ASPNETCORE_ENVIRONMENT=Test` so the
  `FakeAuthHandler` and `/auth/login/fake` (Dev+Test only) become available without dev-mode UI.

## AI Models (Rule 14)
Selection is **per-capability**, not a single three-way category choice: the Studio page's
`AiServicePicker` offers one provider selector per AI capability (analyze image, generate image,
enhance description, style director, scene detail, create audio), each populated from the models
available in that capability's category. `AiProviderIds` (in `PoRedoImage.Shared.Configuration`)
namespaces every provider id by where it executes — `remote:`, `ollama:`, `browser:` — so a
selection stamped onto a request self-describes its execution location without a separate lookup.
The three categories a given capability's models are drawn from:
1. **Remote** — Azure OpenAI (gpt-4o chat/vision) + Google Gemini (image generation)
2. **Web Browser** — Models that run client-side on a Web Worker via WebGPU (falling back to
   WebAssembly). Implemented in `src/PoRedoImage.Client/LocalAi/`: `LocalModelRegistry` is the
   catalog, `DtypeChain` owns the §5 dtype fallback (probe-time pruning against `shader-f16` /
   WebGPU availability, then advance-on-failure through the pruned chain), and
   `JsLocalInferenceRuntime` dispatches to one of two workers — WebLLM for text, transformers.js
   for vision. Both workers speak the same postMessage protocol, and **all fallback policy lives
   in C#** so two runtimes never become two policies.
3. **Ollama** — Local models (dev only, requires Ollama running locally)

**Browser-local execution currently covers Analyze image only.** When the user picks a
`browser:`-prefixed model for that capability, `FeaturePageBase.BuildAnalysisRequestAsync` runs
`LocalAiService.DescribeImageAsync` in-browser and stamps the result onto
`ImageAnalysisRequest.PrecomputedDescription`/`PrecomputedTags`; the server-side orchestrator then
skips its own vision call entirely. A `LocalInferenceException` is surfaced to the user verbatim
(no silent fallback to a metered remote provider — that would be a billing surprise for a user who
deliberately picked a free on-device model). The other capabilities (enhance description, style
director, scene detail, create audio, generate image) do not yet have a browser-local execution
path; a `browser:` id for text (`AiProviderIds.BrowserQwen25`) exists but is deliberately not
offered in the catalog. The original design spec proposed extending browser execution to
description enhancement too, but that was deliberately dropped from scope.

## Image Generation Provider (feature flag + per-request router)
`IImageGenerationService` has two real implementations, both registered so a config flip needs no
redeploy: `GeminiImagen3Service` (Gemini/Imagen) and `HuggingFaceImageGenerationService` (FLUX/Qwen via
HuggingFace Inference Providers). Which one actually runs a given request is decided in two layers:
- **`ImageGen:Provider`** (`"google"` or `"huggingface"`) is the server-side default — read once at
  startup in `InfrastructureServiceExtensions` and baked into `IImageGenerationRouter` as its
  fallback. **`appsettings.Development.json` and `appsettings.Production.json` both currently set
  `huggingface`** — neither environment defaults to Google today.
- **`IImageGenerationRouter.Resolve(ImageAnalysisRequest.ImageGenModelId)`** (in
  `PoRedoImage.Infrastructure.Services.ImageGenerationRouter`) is what the orchestrator actually calls.
  A recognised id (`AiProviderIds.GeminiImagen3` / `HuggingFaceFlux`) picks that provider directly;
  `null` or an unrecognised id falls through to the `ImageGen:Provider` default above. The picker on
  the client (`AiServicePicker` / `AiCapability.GenerateImage`) lets a user pick explicitly, and
  `AiSelectionState.EnsureInitializedAsync` seeds the *un*-picked case from `GET /api/pricing` so the
  client's default and the server's configured default agree — degrading to sending no id at all
  (`GetExplicit` returns `null`) if that seed fetch fails, rather than guessing.
- HuggingFace is routed **fal-native** as `{BaseUrl}/{Provider}/{providerId}` with
  `{prompt, image_url?}` → `{images:[{url}]}`. Text-to-image = **FLUX.1-schnell**
  (`fal-ai/flux/schnell`, ~$0.003/img); image-to-image = **Qwen-Image-Edit**
  (`fal-ai/qwen-image-edit`, ~$0.02/img) — both NON-GATED (verified live 2026-07-11). FLUX.1-Kontext-dev is
  higher quality but **gated** (needs license accept + a gated-repo-scoped token). NOTE: only `hf-inference`
  uses the `/{provider}/models/{hf-id}` path with raw-byte responses; external providers (fal/replicate)
  need their own `providerId` (from `…/api/models/{repo}?expand[]=inferenceProviderMapping`) + native body.
  Set `HuggingFace:ApiKey` (token with the *Inference Providers* permission). Google/Gemini costs
  roughly 13× more per image (~$0.039 vs ~$0.003), so an id or config value that resolves to Gemini
  when HuggingFace was intended is a real cost (or hard-failure, if `Google:ApiKey` is unset) bug, not
  just a labeling one.
- **Pricing UI**: `GET /api/pricing` returns the active provider + indicative per-image prices from the
  `AiPricing` config section; the Bulk Generate page shows "≈ $X/image" plus a running session estimate.
  This is also the seed source for the client's per-request default (above).
- Mock mode (`Mocks:UseMockAi=true`) still wins and swaps in `MockImagen3Service` for either provider.

---

# Architectural Reference

> Merged from the former `docs/AGENT.md` so §6 of NET_RULES holds: **one** living
> architectural source of truth. `docs/AGENT.md` is now a pointer to this file.

## 2. System Topology

```
┌─────────────────────────────────────────────────────────────────────┐
│ Browser (mobile-first, portrait-primary)                           │
│   └── Blazor WASM (PoRedoImage.Client)  ─── global InteractiveWASM │
└──────────────────────────┬──────────────────────────────────────────┘
                           │ HttpOnly+SameSite=Strict cookies
                           │ X-Session-ID · X-Correlation-ID
                           ▼
┌─────────────────────────────────────────────────────────────────────┐
│ ASP.NET Core BFF / API host (PoRedoImage.Web)                      │
│   • Vertical Slice endpoints under Features/{Name}/                │
│   • OIDC challenge (Microsoft Entra) + cookie auth                 │
│   • Minimal API + AuthorizationOptions.FallbackPolicy = RequireAuth│
│   • Forwarded-headers, correlation middleware, Serilog context      │
└────┬──────────────────┬──────────────────┬─────────────┬───────────┘
     │                  │                  │             │
     ▼                  ▼                  ▼             ▼
┌─────────┐       ┌──────────┐      ┌───────────┐  ┌──────────┐
│ CV      │       │ OpenAI   │      │ Gemini    │  │ Table    │
│ eastus  │       │ gpt-4.1  │      │ Imagen3   │  │ Storage  │
└─────────┘       └──────────┘      └───────────┘  └──────────┘
                                                  ┌──────────┐
                                                  │ Key Vault│
                                                  └──────────┘
```

---

## 3. Strict Tech Stack (enforce, don't bend)

| Concern             | Rule                                                                   |
|---------------------|------------------------------------------------------------------------|
| Framework           | **.NET 10** (pinned via `global.json`)                                 |
| Language            | **C# 14** — the version .NET 10 ships; `<LangVersion>latest</LangVersion>` |
| Client              | **Blazor WASM** (`global InteractiveWebAssembly`, **no prerender**)    |
| UI library          | **Radzen Blazor** ("Bento Box" layout)                                 |
| Server host         | **ASP.NET Core BFF** (`PoRedoImage.Web`) — minimal-API endpoints      |
| Persistence         | **Azure Table Storage** (Azurite locally)                              |
| Auth                | Microsoft Entra ID OIDC + dev cookie (`/dev-login`, GUEST mode)       |
| Telemetry           | OpenTelemetry → Application Insights + Serilog (zero-alloc logging)   |
| Tracing             | `X-Session-ID` (per tab) + `X-Correlation-ID` (per request)           |
| Versioning          | MinVer via Git tags                                                    |
| Package mgmt        | **Central Package Management** (`Directory.Packages.props`)            |
| Code quality gates  | **TreatWarningsAsErrors** + **Nullable** enabled globally              |
| AOT                 | **Disabled** (interpreted WASM is faster to ship + CI/CD)              |
| Dead-code policy    | **Zero-Waste**: delete unused files immediately                        |
| Trimming            | `<EnableTrimAnalyzer>` on; everything in `<sln>.Shared` must be trim-safe |

> **Architecture law:** Vertical Slice Architecture (VSA) on the server wins over Onion whenever both would apply. Onion is reserved for cross-slice primitives (`Domain`, `Application`, `Infrastructure`).

---

## 4. Stateful Workflow Loops

### 4.1 Build → Test → Render loop (agents)

1. **Read context**: `AGENT.md`, `README.md`, `PRD_Master.md`, the slice's source.
2. **Plan**: enumerate endpoints, DTOs, validators, EF/Storage entities.
3. **Implement**: feature slice folder under `src/PoRedoImage.Web/Features/{Name}/`.
4. **Validate**: `dotnet build`, `dotnet test tests/PoRedoImage.Tests.Unit`, integration tests with Testcontainers.
5. **Document**: amend `PRD_Master.md` (slice table) + the relevant `.mmd` diagram.

### 4.2 BFF auth invariant (enforced on every commit)

- WASM **never** holds tokens. Only the claims-only `AuthenticationState` is serialized to the client (`AddAuthenticationStateDeserialization`).
- Cookie is `HttpOnly`, `SameSite=Strict`, `Secure=Always` outside Development.
- `AuthorizationOptions.FallbackPolicy = RequireAuthenticatedUser` — every endpoint requires auth unless it explicitly `.AllowAnonymous()`.

### 4.3 Memory hygiene

- **User memory** (`/memories/`): cross-workspace preferences, recurring gotchas.
- **Session memory** (`/memories/session/`): in-flight task notes.
- **Repo memory** (`/memories/repo/`): project-specific facts (build commands, conventions).
- Load before every non-trivial task; update immediately after a finding.

---

## 5. Render Model Rules

- `Components/App.razor` on the server is **only a host document** — it does not ship to the browser.
- All interactive components live under `src/PoRedoImage.Client/`. The server's `_Imports.razor` + `App.razor` exists only to render the Client's `<Routes>`/`<HeadOutlet>` with `RenderModes.WasmNoPrerender`.
- `Layout.WasmNoPrerender` is the canonical render mode for every Client page — never set `RenderMode.InteractiveServer` on a page; everything is WASM.

---

## 6. Anti-patterns (will fail review)

| Anti-pattern                              | Why                                               |
|-------------------------------------------|---------------------------------------------------|
| Adding `AddOidcAuthentication()` on `.Client` | Forbids tokens on the browser                     |
| New onion layer for a feature             | Violates VSA-wins-over-Onion                      |
| `RenderMode.InteractiveServer` on a page  | Breaks the no-prerender contract                  |
| Hard-coded secrets in appsettings         | Use Key Vault references + user-secrets locally   |
| Raw `Configuration["literal"]`            | Use `ConfigKeys` constants; prefer `IOptions<T>` for validated sections |
| New package not in `Directory.Packages.props` | Violates central package management              |
| `Task.Result` / `.Wait()`                 | Async-all-the-way; use `await`                    |
| AOT-only APIs without `[DynamicallyAccessedMembers]` | Will break interpreted WASM at runtime   |

---

## 7. Command Map

```powershell
# build & test
dotnet build PoRedoImage.slnx
dotnet test tests/PoRedoImage.Tests.Unit
dotnet test tests/PoRedoImage.Tests.Integration

# dev loop
dotnet run --project src/PoRedoImage.Web        # http://localhost:4000

# test slices
dotnet test tests/PoRedoImage.Tests.E2E.ApiSmoke
dotnet test tests/PoRedoImage.Tests.E2E.UI

# one-time setup
pwsh -File SCRIPTS/setup.ps1
```