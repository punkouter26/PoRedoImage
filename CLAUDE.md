# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Source of truth

[AGENT.MD](AGENT.MD) is the living architectural reference (368 lines) and wins over this file
on any conflict. Read it before non-trivial work on auth, the render model, or AI providers —
it carries the reasoning behind rules this file only states. [README.md](README.md) is a PRD, not
a dev guide, and its test commands are stale (see below).

## Commands

```powershell
dotnet build PoRedoImage.slnx
dotnet run --project src/PoRedoImage.Web           # http://localhost:4000 | https://localhost:4001
```

Ports 4000/4001 are fixed in [launchSettings.json](src/PoRedoImage.Web/Properties/launchSettings.json)
and the E2E suites default to 4000 — don't change them casually.

```powershell
dotnet test tests/PoRedoImage.Tests.Unit           # pure logic, no I/O, runs in <1s
dotnet test tests/PoRedoImage.Tests.Integration    # needs Docker (Testcontainers spins up Azurite)
dotnet test tests/PoRedoImage.Tests.E2E.ApiSmoke   # pure HTTP; self-skips with no live instance
dotnet test tests/PoRedoImage.Tests.E2E.UI         # Playwright; self-skips with no live instance

dotnet test --filter "FullyQualifiedName~SomeTestClass.SomeMethod"   # single test
dotnet test tests/PoRedoImage.Tests.Unit --filter "FullyQualifiedName~AuthTests"

pwsh ./SCRIPTS/run-e2e.ps1     # builds, launches mock-AI server, runs E2E, tears down
pwsh ./SCRIPTS/setup.ps1       # one-time machine setup (SDK, Docker, Playwright browsers)
```

README.md's `dotnet test tests/PoRedoImage.Tests.E2E` and its `Tests.E2EAPI`/`Tests.E2EUI` names
are stale — the real projects are `Tests.E2E.ApiSmoke` and `Tests.E2E.UI`.

Playwright browsers install once after a build:
`pwsh tests/PoRedoImage.Tests.E2E.UI/bin/Release/net10.0/playwright.ps1 install`

## Mobile (MAUI Android)

`src/PoRedoImage.Mobile` is a MAUI Android head. It is the only project using `TargetFrameworks`
(plural), which matters: [Directory.Build.props](Directory.Build.props) is imported *before* the
project body, so its "no TFM set yet" guard stamps on `TargetFramework=net10.0`. Left in place that
beats `TargetFrameworks` and the solution build tries to build the MAUI head as plain net10.0
(`NETSDK1005`). The csproj clears it with an empty `<TargetFramework></TargetFramework>` — don't
remove that line.

```powershell
dotnet build src/PoRedoImage.Mobile -f net10.0-android -r android-arm64 -t:Install "-p:AdbTarget=-s <serial>"  # phone
dotnet build src/PoRedoImage.Mobile -f net10.0-android -r android-x64   -t:Install "-p:AdbTarget=-s emulator-5554"  # emulator
```

Pick the RID to match the device: physical phones are `arm64-v8a`, the emulator images here are
`x86_64`. Installing the wrong one fails with Android's misleading *"not enough storage space"*.
`AdbTarget` is required whenever more than one device is attached.

**The app talks to the API over HTTP** — it holds no provider keys, by design. The default server URL
is `http://10.0.2.2:4000` on the emulator and `http://localhost:4000` everywhere else, so a USB-
attached phone needs a reverse tunnel or it resolves `localhost` to itself:

```powershell
adb -s <serial> reverse tcp:4000 tcp:4000
```

Untethered, set the PC's LAN IP in the app's Settings tab instead.

**CI does not run tests.** [deploy.yml](.github/workflows/deploy.yml) is build + publish + deploy
only, by explicit policy. Nothing catches a broken test but a local run.

## Running without Azure

There is no `dotnet user-secrets` here — the project deliberately has no `UserSecretsId`. Local runs
read the same Key Vault (`kv-poshared`) the deployed app does, via `DefaultAzureCredential`, so
`az login` with `Key Vault Secrets User` is the normal path. `StartupSecretValidator` fails the host
fast and names each unresolved secret. Without Key Vault access:

```powershell
$env:Mocks__UseMockAi='true'; $env:Storage__ConnectionString=''; dotnet run --project src/PoRedoImage.Web
```

`OpenAI:ChatCompletionsDeployment` is intentionally **not** a Key Vault secret — a stale KV copy once
shadowed the live value and caused `404 DeploymentNotFound`. It lives in appsettings, overridden in
production by an app setting in [infra/main.bicep](infra/main.bicep). Don't re-add it to Key Vault.

## Architecture

Two-project web tier, and the split is the thing most likely to trip you up:

- **[src/PoRedoImage.Client/](src/PoRedoImage.Client/)** — the Blazor WASM SPA. Owns `Routes.razor`,
  layout, and **every interactive page**. This is the only assembly that ships to the browser.
- **[src/PoRedoImage.Web/](src/PoRedoImage.Web/)** — an ASP.NET Core BFF/API host. Its
  `Components/App.razor` is a host document only; it renders the Client's `<Routes>`/`<HeadOutlet>`
  with `RenderModes.WasmNoPrerender`. New UI goes in `.Client`, never here.

Server code is **Vertical Slice** under [src/PoRedoImage.Web/Features/](src/PoRedoImage.Web/Features/)
— `{Auth, BulkGenerate, Diagnostics, Idempotency, ImageAnalysis, MemeTemplates, Pricing, RapRoast,
StyleDirector, UserImages}/`, each co-locating endpoint, DTO, and validator. `Domain`/`Application`/
`Infrastructure` supply cross-slice primitives only. **VSA wins over Onion when both would apply**:
a new feature is a new slice, not a new layer.

`src/PoRedoImage.Shared` holds DTOs and FluentValidation shared across the WASM/API boundary, so it
must stay trim-safe.

### Auth (BFF invariant)

The browser never holds a token. The server authenticates with `HttpOnly` + `SameSite=Strict`
cookies and serializes the **claims-only** principal to WASM via
`AddAuthenticationStateSerialization` ↔ `AddAuthenticationStateDeserialization`. The
`WebAssembly.Authentication` package is referenced on `.Client` solely for the deserialization
extension — `AddOidcAuthentication()` and every token-handling API in it are forbidden.

Authorization is **fail-closed**: `AuthorizationOptions.FallbackPolicy = RequireAuthenticatedUser`.
A new endpoint is authenticated unless it explicitly calls `.AllowAnonymous()`. Client `[Authorize]`
is UI-only and enforces nothing.

### Antiforgery — a filter, not the middleware

Every state-changing endpoint group must call `.RequireAntiforgeryValidation()`. `app.UseAntiforgery()`
alone does nothing for this API: it only validates what it recognizes as a form post, so a JSON POST
sails through regardless of `IAntiforgeryMetadata`. Do **not** additionally set
`RequiresValidation = true` — the middleware then marks the request unvalidated and the filter's own
check trips that guard, rejecting even correct tokens. Tokens are identity-bound, so one issued while
anonymous stops validating after sign-in; `AntiforgeryTokenHandler`'s single retry-on-400 exists for
exactly that and is normal on a first authenticated write.

### AI providers, and how they fail

`IImageGenerationService` has one real implementation: `GeminiImagen3Service`.
`IImageGenerationRouter.Resolve(...)` maps every id — recognized, unrecognized, or null — to Gemini;
the indirection is kept as the slot where a second provider returns. `ImageGen:Provider` is vestigial.
HuggingFace was removed in 2026-08 and must not return without an explicit decision.

`IChatCompletionService` has one real implementation, `AzureOpenAiChatCompletionService`. A single
deployment serves both reasoning and image-to-text.

**The failure mode to understand:** callers of chat/vision/image services catch failures and
substitute canned output. A broken provider therefore degrades several features while `/health` still
reports green — that is exactly what the HuggingFace removal was about. Two live examples: Azure
Computer Vision's `Caption`/`DenseCaptions` are region-unavailable, so `AzureVisionService` falls back
to joined tags on *every* call; and when the vision call itself fails (429 is common), `SceneDescriber`
falls back to that same tag list. **Any new fallback path must set a user-facing reason** (see
`RapRoastResponse.DescriptionFallbackReason`) — silent degradation reads to the user as "the AI
ignored my photo".

Model selection is per-capability, not one global choice. `AiProviderIds` namespaces every id by
execution location (`remote:`, `ollama:`, `browser:`). Browser-local execution
([src/PoRedoImage.Client/LocalAi/](src/PoRedoImage.Client/LocalAi/), WebGPU with WASM fallback)
currently covers **analyze image only**; a `LocalInferenceException` surfaces verbatim rather than
falling back to a metered provider, because silently billing a user who picked a free on-device model
is a surprise charge.

## Build gates

[Directory.Build.props](Directory.Build.props) applies to everything: net10.0, `Nullable`,
`TreatWarningsAsErrors`, `EnableTrimAnalyzer` (diagnostics only — `IsTrimmable` stays off), and NuGet
audit at `low` so a vulnerable package breaks the build. Packages are centrally managed in
`Directory.Packages.props` — a `PackageReference` with an inline version will not build. Versioning is
MinVer from git tags.

Trim analysis is off for test projects only.

## Will fail review

| Anti-pattern | Why |
|---|---|
| Interactive component in `.Web` | All UI lives in `.Client` |
| `RenderMode.InteractiveServer` on a page | Breaks the no-prerender contract |
| `AddOidcAuthentication()` on `.Client` | Puts tokens in the browser |
| New Onion layer for a feature | VSA wins over Onion |
| State-changing endpoint group without `.RequireAntiforgeryValidation()` | Unprotected write on a cookie-auth API |
| Raw `Configuration["literal"]` | Use `ConfigKeys` constants; prefer `IOptions<T>` |
| `ConfigurationBinder.GetValue<T>` | IL2026 under the trim analyzer — use `ConfigValue` |
| `<UserSecretsId>` / `dotnet user-secrets` | Secrets come from Key Vault, local and Azure alike |
| `Task.Result` / `.Wait()` | Async all the way |
| Leaving dead code behind | Zero-Waste policy — delete it in the same change |

## Testing conventions

Four tiers with method ceilings of 100/50/25 enforced by `TestCountCeilingTests` in each project
(counting logic is shared via [tests/TestCounting.cs](tests/TestCounting.cs); a `[Theory]` counts once).
Adding tests past a ceiling breaks the build — consolidate rather than raising it silently.

Integration and E2E run under `ASPNETCORE_ENVIRONMENT=Test`, which swaps in `FakeAuthHandler`
(header-driven `X-Fake-User`/`X-Fake-Roles`); that handler throws if constructed in Production.

Integration tests own a throwaway Azurite container via Testcontainers. The
[docker-compose.yml](docker-compose.yml) Azurite is for the F5 dev loop **only** — tests must never
touch it, so a stale dev container can't leak state into a run. `E2E_BASE_URL` overrides the E2E target.

## Note

AGENT.MD and several files cite ADRs by number (ADR-019, ADR-025, ADR-031). No ADR document exists in
the repo — those citations currently have no backing file.
