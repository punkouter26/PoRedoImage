# Rap Roast — Design

**Date:** 2026-07-27
**Status:** Approved

## Problem

Turn an uploaded photo into a rap track that roasts the people in it — vision to interpret the
image, an LLM to write the bars, and a music model to perform them.

## Feasibility (verified 2026-07-27, not assumed)

The original plan was HuggingFace for everything. Querying the live HF API showed that does not
work:

| Need | HF Inference Providers | Verdict |
|---|---|---|
| Vision | Qwen-VL (already wired) | ✅ |
| Lyrics | any chat model (already wired) | ✅ |
| Music **with sung/rapped lyrics** | none — `text-to-audio` with a live provider is only `Lightricks/LTX-2` (a video model) and `stabilityai/stable-audio-3-medium` | ❌ |

`stable-audio-3-medium` is instrumental (`music`, `sound-effects`) — its prompt *describes* the
audio, it does not perform supplied lyrics — and it is **gated**, which the project has
deliberately avoided since FLUX.1-Kontext-dev (AGENT.MD). No non-gated music model with a live
provider exists at all.

**Google Lyria 3 does what was actually asked**, and it is the fallback the request already
specified:

- Performs **user-supplied lyrics verbatim**, structured with `[Verse]` / `[Chorus]` / `[Bridge]`
  section tags.
- Accepts image input (up to 10) as additional musical inspiration.
- `lyria-3-clip-preview` (~30 s, MP3) and `lyria-3-pro-preview` (multi-minute, WAV).
- `POST https://generativelanguage.googleapis.com/v1beta/interactions` — the **same host and
  `Google:ApiKey`** the app already uses for Gemini.

Source: <https://ai.google.dev/gemini-api/docs/music-generation>

**Chosen:** `lyria-3-clip-preview`. Vision stays on the existing router (HuggingFace locally,
Azure in production), so the HuggingFace-first preference holds everywhere it can.

## Non-Goals

- Client-side audio mixing. Lyria returns a finished track; there is nothing to mix.
- Lyria Pro / full-length songs. A 30 s verse is the right unit for sharing; Pro is a later flag.
- Passing the photo to Lyria *instead of* a vision step — the lyrics must be grounded in a
  description that can be guardrailed before they reach the music model.

## Pipeline

```
photo ─→ IVisionServiceRouter.Resolve(modelId).AnalyzeAsync()        [existing]
              ↓ description + tags
      ─→ IGenerativeAiService.WriteRoastLyricsAsync()                [new method]
              ↓ [Verse]/[Chorus]-tagged bars
      ─→ IMusicGenerationService.GenerateAsync(lyrics, style)        [new]
         LyriaMusicService → v1beta/interactions
              ↓ MP3 bytes + the lyrics
```

## Components

**Slice:** `src/PoRedoImage.Web/Features/RapRoast/` — `RapRoastEndpoints.cs` exposing
`POST /api/rap-roast`. DTOs in `PoRedoImage.Shared/DTOs/RapRoastDtos.cs`. No cross-slice
references (§2).

**`IMusicGenerationService`** (Domain) — vendor-agnostic, mirroring `IImageGenerationService` so
the music provider can be swapped or mocked independently.

**`LyriaMusicService`** (Infrastructure) — sits beside `GeminiImagen3Service` and reuses the
`GeminiApi` named client, inheriting the standard resilience pipeline, the
`MockAiDelegatingHandler` budget guardrail, and `OutboundCorrelationHandler` for free. New config
key `Google:LyriaModel`, added to `ConfigKeys`.

**`RapRoastOrchestrator`** (Application) — owns the three-stage pipeline and the refusal state
machine.

## Content guardrail

The roast targets styling, pose, expression, and vibe. It never targets race, ethnicity,
disability, body weight, age, or religion. This is a product decision *and* a practical one:
Lyria applies safety filters to every prompt, so an over-the-line roast is refused upstream
regardless of what the app sends.

## Refusal handling

Lyria will reject some prompts. The orchestrator is a bounded state machine:

1. Generate lyrics → call Lyria.
2. On refusal, regenerate one deliberately tamer lyric pass and retry **once**.
3. Still refused → return `AudioRefused = true` with the lyrics; the page renders them and
   explains the audio was declined.

At most two Lyria calls per request. The user always receives something.

## Client

`Pages/RapRoast.razor` + scoped CSS, reached from a fifth Studio feature card. Reuses
`ImageSessionService`, so a photo already loaded in Studio flows straight in. Renders
`<audio controls>` plus the lyrics, and the lyrics-only state when audio was refused.

## Mocking

`MockLyriaMusicService : IMusicGenerationService, IMockable` returns a short silent MP3, so
`Mocks:UseMockAi=true` covers the feature and no test can spend a token (§5). Registered in the
same mock block as the other AI services, and — per the bug found during the audit — the mock
must be registered against the interface the orchestrator actually resolves.

## Testing

Headroom at time of writing: Unit 15, Integration 9, API 14, UI 15.

- **Unit (~6):** lyric-prompt guardrail shape, refusal → soften → retry → give-up transitions,
  DTO round-trip, `IsConfigured` behaviour when `Google:ApiKey` is absent.
- **Integration (~2):** endpoint returns 200 with mocks wired; 400 on an invalid image.
- **E2E (~2):** one API smoke (endpoint gated when anonymous), one UI (page reachable, axe-clean).

## Consequences

- Lyria 3 is **preview** — the contract may change without notice.
- Every generated track carries a **SynthID watermark**.
- Production currently sets `ImageGen:Provider=huggingface`; this feature is independent of that
  flag and always uses Google, so `Google:ApiKey` must remain provisioned.
