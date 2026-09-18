# SPEC.md — PoRedoImage as-built

> **Spec of record for the app as it exists today.** Verified against the running app on
> 2026-09-17 (`master` @ `7f44cc2`). `README.md` is the original PRD and is kept for intent;
> where the two disagree this spec wins, and the drift is listed under
> [Open questions](#open-questions). `CLAUDE.md` remains the authority on agent rules and
> architecture invariants — this spec does not override it.

---

## 1. Objective

PoRedoImage is a cloud-native AI multimedia studio: upload a photo, pick a transformation,
get an AI result — regenerated art, a meme, 10 style variations, a performed rap roast, or an
animated video clip — with every artifact saved to a persistent per-user gallery.

Core promise: *upload a photo, choose a transformation, get a gallery-worthy visual or
performed rap roast in under 10 seconds.*

## 2. Verified as-built (evidence, 2026-09-17)

| Check | Result |
|---|---|
| `dotnet run --project src/PoRedoImage.Web` | Boots on `http://localhost:4000`, secret validator passes, OIDC config valid |
| `/alive` | `200 Healthy` |
| `/health` | `Healthy` — all five dependencies (key-vault, computer-vision, openai, table-storage, imagen3). Was `Degraded` until the compose Azurite was started (`docker compose up -d azurite`); the degraded state named the broken dependency and which features were disabled, per the no-silent-degradation rule |
| Studio (`/`) | Renders 5 feature cards, per-capability AI provider pickers, upload + camera capture |
| Video (`/video`) | Renders: Veo 3.1 Lite, 720p, ~$0.40 / 8-second clip, Quick/Standard/Detailed prompt detail |
| Gallery (`/gallery`) | Authenticated API returns a proper empty state with kind filters (All/Originals/Regen/Memes/Bulk) and tag/filename search |
| Auth | Guest cookie session auto-establishes; a cookie issued before an app restart is rejected once, then the session re-establishes on next navigation |

## 3. User journeys

1. **Studio hub** — land on `/`, upload or camera-capture a photo (JPG/PNG ≤ 20 MB); the
   photo context carries into every feature page.
2. **Regenerate** — Gemini recreates the photo as a new artistic version.
3. **Meme** — AI writes a caption; SkiaSharp overlays it on the image.
4. **Bulk Generate** — up to 10 art-style variations in parallel, results stream in as each
   slot completes; 2×2 pin-comparison matrix via Radzen dialog.
5. **Rap Roast** — AI writes a roast verse about the photo, then performs it over a
   generated beat (Lyria).
6. **Video** — prompt + photo in, 8-second Veo clip with sound out; prompt detail tiers
   control clause count.
7. **Gallery** — browse/organize/chain/batch-export everything ever saved, per user,
   across sessions.
8. **Diagnostics** — `/diag` (RadzenDataGrid health rows, masked config), `/health`,
   `/scalar/v1` API docs.
9. **Analyze first** — every feature can analyze the photo first; analysis provider is
   user-selectable per capability (see §4).
10. **Local AI** — browser-local (WebGPU, WASM fallback) analyze and caption options run
    free on-device; failures surface verbatim and never fall back to a metered provider.

## 4. AI providers (per-capability selection)

| Capability | Providers offered |
|---|---|
| Analyze image | Azure Computer Vision · Azure OpenAI vision · Gemini Vision · Florence-2 (browser, ~230 MB) · Ollama (local service) |
| Enhance description/captions | Azure OpenAI · Qwen2.5 0.5B Instruct (browser, ~350 MB) |
| Image generation | Gemini (sole provider; router maps every id to it) |
| Music (roast beat) | Lyria |
| Video | Veo 3.1 Lite, 720p, ~$0.40 per 8-second clip |

Invariants: ids are namespaced by execution location (`remote:` / `ollama:` / `browser:` /
`device:`); each vision backend gets its own cache wrapper keyed on image content hash;
every fallback path sets a user-facing `*FallbackReason`; mock AI (`Mocks:UseMockAi`) is
Test-env-only and renders a "USING MOCK DATA" banner.

## 5. Pinned stack

| Layer | Technology |
|---|---|
| Framework | .NET 10 (`global.json`), Blazor Web App — WASM SPA (`.Client`) + ASP.NET Core BFF (`.Web`), no prerender |
| UI library | Radzen.Blazor (DataGrid, Dialog, Drawer, Notification, TextBox/Button) — retinted in `app.css`; **Radzen first** for any new UI |
| Server layout | Vertical slices under `src/PoRedoImage.Web/Features/` — minimal APIs, `MapGroup`, co-located DTO + validator; no MVC controllers |
| Shared contract | `PoRedoImage.Shared` (DTOs + FluentValidation), one `SharedJsonOptions` resolver chain both sides |
| Data | Azure Table Storage (dev: compose Azurite on `127.0.0.1:10000–10002`) |
| Secrets | Azure Key Vault `kv-poshared` via `DefaultAzureCredential`; **no `dotnet user-secrets`** (deliberate — see CLAUDE.md) |
| Observability | Serilog + OpenTelemetry → App Insights; `/health` names every dependency |
| Infra/CI | Bicep (`infra/main.bicep`) + GitHub Actions `deploy.yml` (five test tiers gate the deploy) |
| Mobile | MAUI Android head in `PoRedoImage.Mobile.slnx`; talks to the API over HTTP only |

## 6. Commands

```powershell
dotnet build PoRedoImage.slnx
dotnet run --project src/PoRedoImage.Web          # http://localhost:4000 | https://localhost:4001 — always restart & verify after changes
docker compose up -d azurite                      # dev-loop storage; /health degrades without it

dotnet test tests/PoRedoImage.Tests.Unit          # <1s, no I/O
dotnet test tests/PoRedoImage.Tests.Integration   # needs Docker (Testcontainers Azurite)
dotnet test tests/PoRedoImage.Tests.E2E.ApiSmoke  # self-skips without a live instance
dotnet test tests/PoRedoImage.Tests.E2E.UI        # Playwright; self-skips without a live instance
dotnet test tests/PoRedoImage.Tests.Architecture  # rule registry, ~2s

pwsh ./SCRIPTS/run-e2e.ps1                        # build → launch :4000 → E2E → teardown
```

Ports 4000/4001 are fixed; do not change them casually.

## 7. Project structure

```
src/
  PoRedoImage.Client/    # WASM SPA — Routes.razor, all interactive pages, only wwwroot
  PoRedoImage.Web/       # BFF/API host — Features/{Auth,BulkGenerate,Diagnostics,Idempotency,
                         #   ImageAnalysis,MemeTemplates,Pricing,RapRoast,Shared,UserImages,VideoGenerate}
  PoRedoImage.Shared/    # DTOs + FluentValidation + shared JSON resolver
  PoRedoImage.Domain/    # Entities + interfaces
  PoRedoImage.Application/ # Config (ConfigValue/ConfigKeys), cross-slice primitives
  PoRedoImage.Infrastructure/ # Repositories + services
  PoRedoImage.Mobile/    # MAUI Android head (separate .slnx)
tests/                   # Unit · Integration · E2E.ApiSmoke · E2E.UI · Architecture
infra/                   # main.bicep + storage lifecycle
SCRIPTS/                 # run-e2e, setup, push-mobile-model, cleanup-testcontainers
```

## 8. Code style & conventions

Authority: `CLAUDE.md`. Non-negotiables repeated here because the spec is the onboarding doc:

- New interactive UI goes in `.Client`, never `.Web`; Radzen controls first.
- `TreatWarningsAsErrors`; NuGet audit at `low`; centrally managed packages (no inline versions).
- Use `ConfigValue.Bool/Float/Double` + `ConfigKeys` — never raw `Configuration["..."]` or
  `ConfigurationBinder.GetValue<T>` (IL2026).
- State-changing endpoint groups call `.RequireAntiforgeryValidation()`; fallback policy is
  fail-closed (authenticated unless `.AllowAnonymous()`).
- Async all the way; zero dead code left behind; master branch only; never push unasked.

## 9. Testing strategy

Five tiers, method ceilings enforced by `TestCountCeilingTests` (a `[Theory]` counts once):
Unit 89/100 · Integration 44/50 · E2E.ApiSmoke 15/25 · E2E.UI 11/25 · Architecture 2/10
(counted 2026-09-07; the ceiling tests recompute in CI). Adding tests past a ceiling breaks
the build — consolidate first. Integration + E2E run under `ASPNETCORE_ENVIRONMENT=Test`
with `FakeAuthHandler`; CI runs all five tiers and gates deploy on them.

## 10. Boundaries

**Always** — restart the app and verify after code changes; keep SPEC/todo docs in sync;
set a user-facing reason on every fallback; apply Radzen first.

**Ask first** — raising a test ceiling; changing ports; adding a new AI provider or fallback
path; touching auth/antiforgery invariants; anything that costs live AI tokens.

**Never** — `dotnet user-secrets`/`<UserSecretsId>`; tokens in the browser
(`AddOidcAuthentication` on `.Client`); prerendering or `InteractiveServer`; new Onion layers
for features; branches other than `master`; push without being asked; silent degradation;
HuggingFace provider (removed by decision).

## 11. Out of scope

Real-time collaboration · custom model fine-tuning UI · server-side prerender/SEO ·
provider keys in the mobile app · re-adding the Gemini "fast tier" without its configuration.

## 12. Edge cases & error states

- **Storage unreachable** → `/health` degrades *and names it*; gallery/prompt persistence
  disabled; everything else still works (verified today).
- **Provider failure** → canned-output substitutions must set `DescriptionFallbackReason`
  (or equivalent); 429 vision failures degrade to tag lists *with a reason*.
- **On-device model missing/fails** → `LocalInferenceException` /
  `OnDeviceCaptionException` surface verbatim; no silent metered fallback.
- **Stale auth cookie after app restart** → one 401, then the guest session re-establishes;
  antiforgery token refresh-on-400 is expected on first authenticated write.
- **Unrecognized AI provider id** → router resolves to the default rather than erroring;
  priced choices must never silently resolve to a different option.

## 13. Measurable success criteria

| # | Criterion | Target |
|---|---|---|
| 1 | Single image regeneration latency | < 10 s p95 |
| 2 | Bulk generate (10 variations) wall clock | < 45 s p95 |
| 3 | `/health` reports every dependency truthfully | 100% of degradations named |
| 4 | CI deploy gate | all five test tiers green |
| 5 | Fallback transparency | every fallback sets a user-facing reason |
| 6 | Build | zero warnings (`TreatWarningsAsErrors`) |
| 7 | Test ceilings | no tier exceeds its method cap |

## 14. Cost picture (indicative USD — `appsettings.json` → `/api/pricing`)

| Action | Cloud path | Cost | Free path |
|---|---|---|---|
| Analyze image | Azure CV · Gemini Vision | ~$0.001 · ~$0.0003 | Florence-2 browser (~230 MB) or Ollama — $0 |
| Enhance/captions | Azure OpenAI | ~$0.0015 | Qwen2.5 0.5B browser (~350 MB) — $0 |
| Regenerate | Gemini 2.5 Flash Image | $0.039/image | none |
| Meme | analyze + enhance, SkiaSharp overlay | ~$0.0025 | compositing is local either way |
| Bulk ×10 | Gemini ×10 | ~$0.39 | none |
| Rap Roast | vision + reasoning + Lyria beat | ~$0.0425 | none |
| Video | Veo 3.1 Lite, 720p | ~$0.40 / 8 s clip | none |

Typical single regeneration ≈ **$0.0415**. Video is the one order-of-magnitude cost item;
generation capabilities have no local fallback by design. These are indicative UI estimates,
not billed amounts. **Cost posture: cloud-first** (decided 2026-09-17) — live cloud AI stays
the default; browser-local/Ollama models remain opt-in extras for analyze/captions only.

## 15. Decisions (2026-09-17)

1. Vision confirmed: AI multimedia studio, as built.
2. Current phase: **harden & polish** — no new feature surface.
3. Mobile (MAUI Android) is a **first-class** target; web changes must not strand it.
4. README PRD drift: **fixed** (non-goals, feature table, CI claim corrected).
5. Cost posture: **cloud-first** — decided 2026-09-17 after reviewing §14.
6. **Mobile parity audit (2026-09-17): the MAUI app is NOT up to date with the web client.**
   Gaps found in `MobileApiClient.cs` / `MainViewModel.cs` (mobile calls only
   `api/images/analyze`, `api/rap-roast`, `api/bulk-generate/describe`, `alive`):

   | Capability | Web | MAUI |
   |---|---|---|
   | Camera/upload | ✅ | ✅ |
   | Analyze | ✅ per-capability pickers, 5 providers | ✅ server default only |
   | Meme | ✅ | ✅ + on-device Qwen captions (mobile-exclusive) |
   | Regenerate | ✅ Gemini image via `/api/bulk-generate/batch`·`/reroll` | ❌ analyze/prompt only — never generates an image |
   | Bulk ×10 + matrix | ✅ streamed `/batch` + compare matrix + reroll | ❌ `/describe` only |
   | Rap Roast | ✅ | ✅ |
   | Video (Veo) | ✅ | ❌ no `/api/video` call |
   | Gallery persistence | ✅ `/api/user-images` | ❌ results are ephemeral |
   | Diagnostics | ✅ `/diag` | ❌ Settings page only |
   | Auth | ✅ guest/Entra cookie | ⚠️ none — `EnsureAuthenticatedAsync` only pings `/alive`; unverified against the fail-closed fallback policy |

   Closing Regenerate, Bulk, Video, Gallery, and the auth story is the natural first work of
   the "mobile is first-class + harden & polish" phase.

   **Status 2026-09-18:** parity work SHIPPED — real Regenerate (Gemini via `/batch`), Bulk ×10
   with live NDJSON board, Veo video with polling + completion notification, gallery save +
   Gallery tab, guest-session auth (Dev/Test servers), share-target inbound photos, quick-settings
   tile, biometric gallery lock, on-device model catalog (0.5B/1.5B/Phi-3-mini/Llama-3.2-3B),
   roast fallback-reason surfacing. Android target builds clean.

   **Deferred:** home-screen widget (`Xamarin.AndroidX.Appwidget` is not published on NuGet;
   the quick-settings tile ships instead), CameraX pro-capture (torch/tap-to-focus — binding API
   surface did not match current docs; Pro Shot button falls back to MediaPicker), FCM push
   (needs a Firebase project `google-services.json` + a server-side push slice; the local
   notification half ships via the render foreground service).

## 16. Open questions (remaining)

1. **Studio bulk card copy** says "via Gemini 2.0 Flash"; config and PRD say Gemini 2.5
   Flash Image. Which label is right?
2. **Dev login** — README documents `/dev-login?email=…`; the app also auto-establishes a
   guest session. Is the email dev-login still a supported path?
3. **Latency metrics** — PRD targets (<10 s p95 regen, <45 s bulk) have no dashboard.
   Wire to App Insights, or drop?
4. **Polish item** — `AiPricing.VideoGenerationUsd` is a code default (`0.40m` in
   `AiPricingOptions`) while its siblings live in `appsettings.json`. Hoist to config?
