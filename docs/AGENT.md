---
project: PoRedoImage
tier: 0
type: agent-context
last_updated: 2026-07-11
---

# PoRedoImage — AI Agent Context

> Foundational context layer for autonomous AI coding agents. Read this first.

---

## 1. Vision

PoRedoImage is a **cloud-native AI image studio** that turns ordinary photos into art, memes, and stylistic variations. Chain Azure Computer Vision, Azure OpenAI GPT-4.1-nano, and Google Gemini Imagen3 behind a Blazor Web App — *upload, choose a style, get a gallery-ready result in < 10 s*.

---

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
| Language            | **C# 14** (record types, primary constructors, `field` keyword)        |
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
| `Configuration["..."]` outside Options    | Bind via `IOptions<T>` + `IValidateOptions<T>`   |
| New package not in `Directory.Packages.props` | Violates central package management              |
| `Task.Result` / `.Wait()`                 | Async-all-the-way; use `await`                    |
| AOT-only APIs without `[DynamicallyAccessedMembers]` | Will break interpreted WASM at runtime   |
| Tests in CI                               | Policy: CI ships; tests run locally + dedicated workflow |

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