# CLAUDE.md / AGENT.md

This file provides guidance to AI coding agents (Claude Code, Antigravity, etc.) when working with code in this repository.

## Core Agent Rules

1. **Master Branch Only**: Only use the `master` branch for all work and only use other branches if specifically asked to.
2. **Never Push Without Asking**: Never push code to remote without me specifically asking.
3. **Restart and Verify**: Always restart app and verify it restart successfully after making code change.
4. **Project Documentation**: This file is the project summary. There is no `DOCS` folder — it was removed deliberately; do not re-add references to one.
5. **No dotnet secrets**: Do not use dotnet secrets to store data locally / Put it in appSettings or Azure Key Vault (if one exists).
6. **Git Commits in American Slang**: When git sync happen create a git commit that is short and uses american slang so it seems a human wrote it.
7. **Response TLDR**: At the end of any prompt that has an answer longer than 100 words, at a TLDR 20 word summary.

## Source of truth

`CLAUDE.md` is the agent-facing doc in the repo and the overall summary of the project;
`AGENT.md` is a pointer to it, not a second copy (the two were byte-identical 298-line duplicates
and had no mechanism keeping them in sync). [README.md](README.md) is a PRD, not a dev guide, and
its test commands and `docs/` paths are wrong (see below).

There is **no `DOCS/` directory**. It was tracked once and deleted deliberately, along with
`.codescene/`, `CODE-HEALTH-SCORECARD.md` and the `SCRIPTS/generate-scorecard.ps1` that produced
it. Don't cite `DOCS/` or `docs/` as a source of truth — nothing in the repo generates them.

Several code comments cite ADRs by number (ADR-019, ADR-025, ADR-031) and section numbers (§1
Trimming, §2 Security, Po2Logic R3/F7). No such documents exist in this repo — the reasoning lives
in the comment itself, not in a linked file.

## Commands

```powershell
dotnet build PoRedoImage.slnx
dotnet run --project src/PoRedoImage.Web           # http://localhost:4000 | https://localhost:4001
```

> **Important**: Always restart app and verify it restart successfully after making code change.

Ports 4000/4001 are fixed in [launchSettings.json](src/PoRedoImage.Web/Properties/launchSettings.json)
and the E2E suites default to 4000 — don't change them casually.

```powershell
dotnet test tests/PoRedoImage.Tests.Unit           # pure logic, no I/O, runs in <1s
dotnet test tests/PoRedoImage.Tests.Integration    # needs Docker (Testcontainers spins up Azurite)
dotnet test tests/PoRedoImage.Tests.E2E.ApiSmoke   # pure HTTP; self-skips with no live instance
dotnet test tests/PoRedoImage.Tests.E2E.UI         # Playwright; self-skips with no live instance

dotnet test --filter "FullyQualifiedName~SomeTestClass.SomeMethod"   # single test
dotnet test tests/PoRedoImage.Tests.Unit --filter "FullyQualifiedName~AuthTests"

pwsh ./SCRIPTS/run-e2e.ps1     # builds, launches the app on :4000, waits for /alive, runs E2E, tears down
pwsh ./SCRIPTS/setup.ps1       # one-time machine setup (SDK, Docker, Playwright browsers)
pwsh ./SCRIPTS/cleanup-testcontainers.ps1   # reap leaked Azurite containers after an aborted run
```

README.md's `dotnet test tests/PoRedoImage.Tests.E2E` and its `Tests.E2EAPI`/`Tests.E2EUI` names are
stale — the real projects are `Tests.E2E.ApiSmoke` and `Tests.E2E.UI`. Its `docs/` links are dead
too; no documentation directory exists.

Playwright browsers install once after a build:
`pwsh tests/PoRedoImage.Tests.E2E.UI/bin/Release/net10.0/playwright.ps1 install`

**CI does not run tests.** [deploy.yml](.github/workflows/deploy.yml) is build + publish + deploy
only, by explicit policy, and it is the only workflow. Nothing catches a broken test but a local run.

It also builds `src/PoRedoImage.Web` rather than `PoRedoImage.slnx`, and must keep doing so: the
solution contains the MAUI head, whose `net10.0-android` TFM fails restore on `ubuntu-latest` with
`NETSDK1147` (no `maui-android` workload). Pointing CI back at the solution reintroduces that break;
adding the workload instead costs minutes per deploy to produce an APK nothing consumes. The
consequence to keep in mind is that the test projects are not compiled in CI either — a test-only
compile break only shows up locally.

## Running without Azure

Do not use dotnet secrets to store data locally / Put it in appSettings or Azure Key Vault (if one exists). There is no `dotnet user-secrets` here — the project deliberately has no `UserSecretsId`. Local runs
read the same Key Vault (`kv-poshared`) the deployed app does, via `DefaultAzureCredential`, so
`az login` with `Key Vault Secrets User` is the normal path. `StartupSecretValidator` fails the host
fast and names each unresolved secret. Without Key Vault access:

```powershell
$env:Mocks__UseMockAi='true'; $env:Storage__ConnectionString=''; dotnet run --project src/PoRedoImage.Web
```

`Mocks:UseMockAi` swaps in mock vision / generative / image-gen / chat / music services, each
implementing `IMockable` so the client renders a "USING MOCK DATA" banner listing the reasons.

`OpenAI:ChatCompletionsDeployment` is intentionally **not** a Key Vault secret — a stale KV copy once
shadowed the live value and caused `404 DeploymentNotFound`. It lives in appsettings, overridden in
production by an app setting in [infra/main.bicep](infra/main.bicep). Don't re-add it to Key Vault.

## Architecture

Two-project web tier, and the split is the thing most likely to trip you up:

- **[src/PoRedoImage.Client/](src/PoRedoImage.Client/)** — the Blazor WASM SPA. Owns `Routes.razor`,
  layout, and **every interactive page**. This is the only assembly that ships to the browser, and
  the only `wwwroot` in the solution.
- **[src/PoRedoImage.Web/](src/PoRedoImage.Web/)** — an ASP.NET Core BFF/API host. Its
  `Components/App.razor` is a host document only; it renders the Client's `<Routes>`/`<HeadOutlet>`
  with `RenderModes.WasmNoPrerender`. New UI goes in `.Client`, never here.

Prerendering is off deliberately: the WASM client owns the only DI container (`ImageSessionService`,
`IWebAssemblyHostEnvironment`, the deserialized auth state), so a server prerender pass could not
resolve those services. See [RenderModes.cs](src/PoRedoImage.Client/RenderModes.cs).

Server code is **Vertical Slice** under [src/PoRedoImage.Web/Features/](src/PoRedoImage.Web/Features/)
— `{Auth, BulkGenerate, Diagnostics, Idempotency, ImageAnalysis, MemeTemplates, Pricing, RapRoast,
Shared, StyleDirector, UserImages}/`, each co-locating endpoint, DTO, and validator. Minimal APIs
only — no MVC controllers; `MapGroup` plus static handler methods. `Domain`/`Application`/
`Infrastructure` supply cross-slice primitives only. **VSA wins over Onion when both would apply**:
a new feature is a new slice, not a new layer.

`src/PoRedoImage.Shared` holds DTOs and FluentValidation shared across the WASM/API boundary, so it
must stay trim-safe. Serialization runs through `SharedJsonOptions.CreateResolver()` on **both**
sides so the client's call sites and the minimal APIs share one resolver chain.

### Auth (BFF invariant)

The browser never holds a token. The server authenticates with `HttpOnly` + `SameSite=Strict`
cookies and serializes the **claims-only** principal to WASM via
`AddAuthenticationStateSerialization` ↔ `AddAuthenticationStateDeserialization`. The
`WebAssembly.Authentication` package is referenced on `.Client` solely for the deserialization
extension — `AddOidcAuthentication()` and every token-handling API in it are forbidden.

Authorization is **fail-closed**: `AuthorizationOptions.FallbackPolicy = RequireAuthenticatedUser`.
A new endpoint is authenticated unless it explicitly calls `.AllowAnonymous()`. Client `[Authorize]`
is UI-only and enforces nothing.

That fail-closed default has one hand-written escape hatch in `Program.cs`: a middleware that stamps
`AllowAnonymous` onto `/_framework` endpoints. The runtime-generated boot manifest is not a static
asset, so `MapStaticAssets().AllowAnonymous()` misses it, the fallback policy 302s it to `/login`,
and the browser's subresource-integrity check then fails and the app never boots (blank page).
Don't remove it.

Three auth modes, resolved at startup by `AddPoRedoImageAuth`: `Auth:EnableFakeAuth` (test/dev only —
header-driven `FakeAuthHandler`, which throws if constructed in Production), cookie-only when no
`AzureAd:ClientId` is set, and Microsoft Entra OIDC + cookie otherwise. Production without a
ClientId throws by design rather than silently degrading to cookie-only.

### Antiforgery — a filter, not the middleware

Every state-changing endpoint group must call `.RequireAntiforgeryValidation()`. `app.UseAntiforgery()`
alone does nothing for this API: it only validates what it recognizes as a form post, so a JSON POST
sails through regardless of `IAntiforgeryMetadata`. Enforcement lives in `AntiforgeryValidationFilter`
([Features/Shared/](src/PoRedoImage.Web/Features/Shared/)); apply it at the **group** level so a newly
added POST inside an existing group is protected by default rather than by memory.

Do **not** additionally set `RequiresValidation = true` — the middleware then marks the request
unvalidated and the filter's own check trips that guard, rejecting even correct tokens. The client
echoes the token in `X-CSRF-TOKEN` because it cannot read the HttpOnly cookie. Tokens are
identity-bound, so one issued while anonymous stops validating after sign-in;
`AntiforgeryTokenHandler`'s single retry-on-400 exists for exactly that and is normal on a first
authenticated write.

### AI providers, and how they fail

`IImageGenerationService` has one real implementation: `GeminiImagen3Service`.
`IImageGenerationRouter.Resolve(...)` maps every id — recognized, unrecognized, or null — to Gemini;
the indirection is kept as the slot where a second provider returns. `ImageGen:Provider` is vestigial.
HuggingFace was removed in 2026-08 and must not return without an explicit decision.

`IChatCompletionService` resolves **once at startup**, not per request: `OllamaChatCompletionService`
when `Ollama:ChatModel` is set, `AzureOpenAiChatCompletionService` otherwise. A single Azure
deployment serves both reasoning and image-to-text.

`IVisionServiceRouter` *is* per-request, and matches on the id **namespace**, never a model-name
prefix: `ollama:*` → `OllamaVisionService`, `remote:azure-openai-vision` → `OpenAiVisionService`,
everything else (including `browser:*`, which should never have reached the server) →
`AzureVisionService`. Each backend gets its **own** `CachingVisionService` wrapper — the cache key is
the image content hash, so one shared wrapper would serve an Ollama answer to a caller who asked for
Azure.

**The failure mode to understand:** callers of chat/vision/image services catch failures and
substitute canned output. A broken provider therefore degrades several features while `/health` still
reports green — that is exactly what the HuggingFace removal was about. Two live examples: Azure
Computer Vision's `Caption`/`DenseCaptions` are region-unavailable, so `AzureVisionService` falls back
to joined tags on *every* call; and when the vision call itself fails (429 is common), `SceneDescriber`
falls back to that same tag list. **Any new fallback path must set a user-facing reason** (see
`RapRoastResponse.DescriptionFallbackReason`, `StyleDirectorResponse.FallbackReason`) — silent
degradation reads to the user as "the AI ignored my photo".

Model selection is per-capability, not one global choice. `AiProviderIds` namespaces every id by
execution location (`remote:`, `ollama:`, `browser:`, `device:`) — `browser:qwen2.5-0.5b-instruct`
and `device:qwen2.5-0.5b-instruct` are the same weights under different runtimes and must never be
treated as interchangeable. Browser-local execution
([src/PoRedoImage.Client/LocalAi/](src/PoRedoImage.Client/LocalAi/), WebGPU with WASM fallback)
currently covers **analyze image only**; a `LocalInferenceException` surfaces verbatim rather than
falling back to a metered provider, because silently billing a user who picked a free on-device model
is a surprise charge.

## Mobile (MAUI Android)

[src/PoRedoImage.Mobile/](src/PoRedoImage.Mobile/) is a MAUI Android head. It is the only project
using `TargetFrameworks` (plural), which matters: [Directory.Build.props](Directory.Build.props) is
imported *before* the project body, so its "no TFM set yet" guard stamps on `TargetFramework=net10.0`.
Left in place that beats `TargetFrameworks` and the solution build tries to build the MAUI head as
plain net10.0 (`NETSDK1005`). The csproj clears it with an empty `<TargetFramework></TargetFramework>`
— don't remove that line.

```powershell
dotnet build src/PoRedoImage.Mobile -f net10.0-android -r android-arm64 -t:Install "-p:AdbTarget=-s <serial>"  # phone
dotnet build src/PoRedoImage.Mobile -f net10.0-android -r android-x64   -t:Install "-p:AdbTarget=-s emulator-5554"  # emulator
```

Pick the RID to match the device: physical phones are `arm64-v8a`, the emulator images here are
`x86_64`. Installing the wrong one fails with Android's misleading *"not enough storage space"*.
`AdbTarget` is required whenever more than one device is attached.

**The app talks to the API over HTTP** — it holds no provider keys, by design, and references only
`PoRedoImage.Shared`. The default server URL is `http://10.0.2.2:4000` on the emulator and
`http://localhost:4000` everywhere else, so a USB-attached phone needs a reverse tunnel or it
resolves `localhost` to itself:

```powershell
adb -s <serial> reverse tcp:4000 tcp:4000
```

Untethered, set the PC's LAN IP in the app's Settings tab instead.

### On-device meme captions

Meme captions can be written by Qwen2.5 0.5B Instruct (int4) running on the phone through ONNX
Runtime GenAI rather than by the server. Opt-in via *Settings → On-Device Meme Captions*, off by
default, because the ~800 MB of weights are **side-loaded and never bundled** —
`EmbedAssembliesIntoApk` would otherwise try to pack them into the APK.

```powershell
pwsh ./SCRIPTS/push-mobile-model.ps1                                  # download + adb push
pwsh ./SCRIPTS/push-mobile-model.ps1 -SkipPush                        # cache only, no device yet
pwsh ./SCRIPTS/push-mobile-model.ps1 -SkipDownload -Serial <serial>   # push an already-cached copy
```

The target is `/sdcard/Android/data/com.poredoimage.mobile/files/models/...` — the app's **external**
files directory, not `FileSystem.AppDataDirectory`. That is the whole trick: `/data/data` is
unreadable to the adb shell user, so a model pushed there would need root. Install the app before
pushing (the parent is app-scoped storage), and use *Re-check Model* in Settings to re-probe without
restarting.

Qwen2.5 is text-only, so this path still calls the server to describe the photo — only the caption is
local, and the result is the photo plus caption text rather than a composited meme. When the model is
missing or inference fails, `OnDeviceCaptionException` surfaces verbatim and does **not** fall back
to the server, for the same reason `LocalInferenceException` doesn't on the web client.

## Build gates

[Directory.Build.props](Directory.Build.props) applies to everything: net10.0, `Nullable`,
`TreatWarningsAsErrors`, `EnableTrimAnalyzer` (diagnostics only — `IsTrimmable` stays off), and NuGet
audit at `low` so a vulnerable package breaks the build. Packages are centrally managed in
`Directory.Packages.props` — a `PackageReference` with an inline version will not build. Versioning is
MinVer from git tags. Trim analysis is off for test projects only.

Because warnings are errors, IL2026 from the trim analyzer breaks the build. There are three
deliberate `#pragma warning disable IL2026` sites in `Program.cs` (Razor component registration,
options binding, the anonymous health payload), each with a comment for why the reflective path is
required. The options-binding one especially: those option types use `init`-only properties, which
the configuration binding source generator cannot assign, so enabling it silently binds every value
to empty and the host fail-fasts at startup. Everywhere else, use `ConfigValue.Bool/Float/Double`
([ConfigValue.cs](src/PoRedoImage.Application/Configuration/ConfigValue.cs)) instead of
`ConfigurationBinder.GetValue<T>`.

## Testing conventions

**Five** tiers with method ceilings of 100/50/25/25/10 — Unit / Integration / E2E.ApiSmoke /
E2E.UI / **Architecture** — enforced by `TestCountCeilingTests` in each project (counting logic is
shared via [tests/TestCounting.cs](tests/TestCounting.cs); a `[Theory]` counts once regardless of
`InlineData`). Adding tests past a ceiling breaks the build — consolidate rather than raising it
silently.

`PoRedoImage.Tests.Architecture` is easy to miss: it is in the slnx and enforced like the rest, but
went undocumented here for a while. It holds a rule *registry* rather than one method per rule, so
its whole ruleset costs two methods against the ceiling however many rules it grows.

Approximate headroom, so you know which tier can absorb a new test:

| Tier | Methods | Ceiling |
|---|---|---|
| Unit | ~95 | 100 |
| Integration | ~47 | 50 |
| E2E.ApiSmoke | ~10 | 25 |
| E2E.UI | ~11 | 25 |
| Architecture | ~8 | 10 |

Unit and Integration are the tight ones. Integration sat at exactly 50/50 — one test from breaking
the build — until the per-rule pass/fail method pairs in `Contracts/` were folded into single
theories covering both sides. Do that before reaching for the ceiling constant.

Integration and E2E run under `ASPNETCORE_ENVIRONMENT=Test`, which swaps in `FakeAuthHandler`
(header-driven `X-Fake-User`/`X-Fake-Roles`). Note that the environment check gating antiforgery
cookie security is `IsDevOrTest()`, not `IsDevelopment()` — these tiers run over plain HTTP, and
`CookieSecurePolicy.Always` would throw from `GetAndStoreTokens` on every write.

Integration tests own a throwaway Azurite container via Testcontainers. The
[docker-compose.yml](docker-compose.yml) Azurite is for the F5 dev loop **only** — tests must never
touch it, so a stale dev container can't leak state into a run. `E2E_BASE_URL` overrides the E2E
target (default `http://localhost:4000`).

## Branching & Git Sync — master only

- **Master branch only**: Only use the master branch for all work and only use other branches if specifically asked to. Do **not** create feature, fix, or release branches, and do not open pull requests. No other branch may exist locally or on `origin`; if one appears, merge it into `master` and delete it on both sides. This overrides the usual "branch before committing" default — here, committing straight to `master` is the intended workflow, not an accident.
- **Never push without asking**: Never push code to remote without me specifically asking.
- **Commit style**: When git sync happen create a git commit that is short and uses american slang so it seems a human wrote it.

## Will fail review

| Anti-pattern | Why |
|---|---|
| Creating any branch other than `master` without being asked | Master branch only for all work unless specifically asked |
| Pushing code to remote without explicit request | Never push code to remote without me specifically asking |
| Skipping app restart & verification | Always restart app and verify it restart successfully after making code change |
| `<UserSecretsId>` / `dotnet user-secrets` | Do not use dotnet secrets to store data locally / Put it in appSettings or Azure Key Vault |
| Long or robotic commit messages during git sync | When git sync happen create a git commit that is short and uses american slang so it seems a human wrote it |
| Response >100 words missing 20-word TLDR | At the end of any prompt that has an answer longer than 100 words, at a TLDR 20 word summary |
| Interactive component in `.Web` | All UI lives in `.Client` |
| `RenderMode.InteractiveServer`, or prerendering on a page | Breaks the no-prerender contract |
| `AddOidcAuthentication()` on `.Client` | Puts tokens in the browser |
| New Onion layer for a feature | VSA wins over Onion |
| State-changing endpoint group without `.RequireAntiforgeryValidation()` | Unprotected write on a cookie-auth API |
| A new fallback path with no user-facing reason | Silent degradation reads as "the AI ignored my photo" |
| Raw `Configuration["literal"]` | Use `ConfigKeys` constants; prefer `IOptions<T>` |
| `ConfigurationBinder.GetValue<T>` | IL2026 under the trim analyzer — use `ConfigValue` |
| MVC controllers | Minimal APIs + `MapGroup` only |
| `Task.Result` / `.Wait()` | Async all the way |
| Leaving dead code behind | Zero-Waste policy — delete it in the same change |
