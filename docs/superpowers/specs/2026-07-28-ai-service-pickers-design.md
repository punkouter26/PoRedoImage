# Per-capability AI service pickers

- **Date**: 2026-07-28
- **Status**: Design — approved for planning
- **Supersedes**: `ModelCategoryPicker` (three-way category radio group on Studio)

## Problem

Studio renders `ModelCategoryPicker`, a radio group offering Remote / Web Browser / Ollama. It has
two defects:

1. **It is decorative.** `Studio.razor` declares `_modelCategory` at line 116 and binds it at line 34.
   Those are its only two occurrences in the solution — nothing reads the value. Selecting a category
   changes nothing about which service runs.
2. **It models the wrong axis.** A single global category cannot be correct, because the categories
   are not interchangeable across capabilities. Ollama is image-to-text only and cannot generate
   images; the browser registry has no image-generation model either. A user selecting "Ollama"
   cannot meaningfully be asking for Ollama-powered image generation, because no such thing exists.

The app uses six distinct AI capabilities. The right axis is **one selector per capability**.

## Capability matrix

Non-mock providers registered in `InfrastructureServiceExtensions`:

| Capability | Interface | Providers today | Per-request switchable now? |
|---|---|---|---|
| Analyze image | `IVisionService` | Azure Computer Vision, Ollama (`gemma4` default) | Yes — `IVisionServiceRouter` resolves from `ModelId` |
| Generate image | `IImageGenerationService` | Gemini Imagen 3, HuggingFace FLUX.1-schnell | No — `ImageGen:Provider` picks one singleton at startup |
| Enhance description / meme caption | `IGenerativeAiService` | Azure OpenAI | No — single implementation |
| Style Director reasoning | `IChatCompletionService` | HuggingFace | No — single implementation |
| Scene detail (OCR, dense captions) | `ISceneDetailProvider` | Azure Computer Vision | No — single implementation |
| Create audio | `IMusicGenerationService` | Google Lyria 3 | No — single implementation |

Adding the browser-local models from `LocalModelRegistry` gives two capabilities a genuine third
and second option respectively:

- **Analyze image** gains Florence-2 base (`onnx-community/Florence-2-base`, ~230 MB, transformers.js)
- **Enhance description** gains Qwen2.5 0.5B Instruct (~350 MB, WebLLM)

## Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Control shape | One `<select>` per capability, `<optgroup>` grouped by category | Matches the capability axis; groups preserve the Remote / Web Browser / Ollama distinction the old picker communicated |
| Single-provider capabilities | Rendered as a **disabled** `<select>` with one option | Uniform layout; new providers slot in without changing the shape. A `title` states why it is disabled |
| Browser-local models | **Included** as options where they can serve | Gives the dropdowns real choice and finally puts the existing WebGPU detection to use |
| Selection persistence | **Session-only**, no `localStorage` | No stale-selection bugs when a provider is removed; less state to reason about |
| Placement | Replaces `ModelCategoryPicker` in the Studio upload column | One place to configure |

## Architecture

### Client

**`AiCapability`** (enum, `PoRedoImage.Client.Models`) — `AnalyzeImage`, `GenerateImage`,
`EnhanceDescription`, `StyleDirector`, `SceneDetail`, `CreateAudio`.

`EnhanceDescription` covers **both** `IGenerativeAiService` methods — `EnhanceDescriptionAsync` and
`GenerateMemeCaptionAsync`. They share one implementation and one provider, so they get one selector,
not two. The row is labelled "Enhance description &amp; captions" to make that scope visible.

**`AiProviderOption`** (record) — `Id`, `DisplayName`, `Category`, `Hint`, `ExecutesInBrowser`.

**`AiServiceCatalog`** (static) — one entry per `AiCapability` listing its `AiProviderOption`s.
Browser options are **derived from `LocalModelRegistry`** by capability rather than restated, so that
registry remains the single catalog for browser models (NET_RULES §5). Remote and Ollama options are
declared constants.

**`AiSelectionState`** (scoped DI service) — `Dictionary<AiCapability, string>` with catalog defaults.
Scoped in WASM survives navigation and resets on reload, which is precisely the session-only
behaviour required. It must be a service and not Studio-local state: Studio is where the user
chooses, but Regeneration, Meme, Bulk Generate, and Rap Roast are where requests fire.

**`AiServicePicker.razor`** (`Client/Shared`) — renders six labelled rows. Reads and writes
`AiSelectionState`. Retains the existing WebGPU capability line, shown beneath the two rows where a
browser model is selectable. Replaces `ModelCategoryPicker.razor` and its `.razor.css`, both deleted
per the Zero-Waste policy.

### Identifier namespacing (defect fix)

`VisionServiceRouter.IsLocalModel` matches by prefix, treating any id starting with `qwen` as Ollama.
The browser text model is `qwen2.5-0.5b-instruct`. These ids collide. The collision is not reachable
today — browser selections execute client-side and never reach the server — but it is one refactor
away from silently routing work to the wrong backend.

**Every provider id becomes category-namespaced**: `remote:azure-cv`, `remote:gemini-imagen3`,
`remote:hf-flux-schnell`, `remote:azure-openai`, `ollama:gemma4`, `browser:florence2-base`,
`browser:qwen2.5-0.5b-instruct`. `VisionServiceRouter` switches from prefix-guessing to an explicit
`ollama:` check. Ids without a namespace continue to resolve to Azure, preserving compatibility with
any caller that omits `ModelId`.

### Server

**`IImageGenerationRouter` / `ImageGenerationRouter`** (`Domain.Interfaces` / `Infrastructure.Services`)
— mirrors `VisionServiceRouter`. `Resolve(string? modelId)` returns the HuggingFace or Gemini service,
**falling back to the `ImageGen:Provider` config flag when `modelId` is null or unrecognised**. This
keeps current behaviour exactly when the client sends nothing. `ImageAnalysisOrchestrator` takes the
router in place of the `IImageGenerationService` singleton.

Mock mode registers a single-service router mirroring `SingleVisionServiceRouter`, so
`Mocks:UseMockAi=true` continues to win over any client selection.

### DTO changes (`PoRedoImage.Shared`)

`ImageAnalysisRequest` gains three fields. Explicit named fields rather than a dictionary — trim-safe
for `.Shared` and validatable:

- `string? ImageGenModelId` — selected image-generation provider
- `string? PrecomputedDescription` — vision output produced in the browser
- `IReadOnlyList<string>? PrecomputedTags` — tags produced in the browser

`ModelId` (existing) continues to carry the vision selection.

### The split pipeline

`/api/images/analyze` currently runs vision → enhance → generate in one request. When the user picks
a browser model for Analyze image, the client has already produced the description and needs the
server to skip step one.

`ImageAnalysisOrchestrator` gains one branch:

```
if (request.PrecomputedDescription is not null)
    use PrecomputedDescription + PrecomputedTags, ImageAnalysisTimeMs = 0
else
    visionRouter.Resolve(request.ModelId).AnalyzeAsync(...)
```

One endpoint, one branch, no duplicated pipeline. Selecting Qwen2.5 for Enhance description follows
the same shape, with the client calling `LocalAiService.CompleteTextAsync` and posting the enhanced
text.

Confidence for a precomputed description is reported as `1.0`, matching how `OllamaVisionService`
already handles local models that emit no calibrated confidence.

### Client execution path

`LocalAiService` already exposes the two required entry points — `DescribeImageAsync` and
`CompleteTextAsync` — so no new JS interop or worker protocol work is needed.

Feature pages consult `AiSelectionState` before issuing a request. When the selected option has
`ExecutesInBrowser = true`, the page runs the local inference first and posts the result in the
precomputed fields; otherwise it posts as it does today.

## Error handling

| Condition | Behaviour |
|---|---|
| Browser model selected, no WebGPU adapter | Proceeds. `DtypeChain` prunes to WASM-capable variants and runs on CPU. The picker already warns that this is slow but supported — never disabled, since disabling would deny a working configuration |
| Model download stalls or worker crashes | `LocalAiErrorClassifier` maps the failure to a user-facing message; the page surfaces it inline and offers to retry with the remote provider. It does **not** silently fall back — a silent switch to a metered API is a billing surprise |
| `ImageGenModelId` names an unconfigured provider | Router falls back to the `ImageGen:Provider` default rather than throwing |
| `Mocks:UseMockAi=true` | Mock services win over every selection; the picker shows the existing "USING MOCK DATA" banner context |
| Selection references a removed provider | Cannot occur — state is session-only and rebuilt from the catalog on load |

## Testing

Within the per-tier method ceilings (100 / 50 / 25).

**Unit** (`Tests.Unit`)
- `AiServiceCatalog` exposes the expected options per capability, and browser options track
  `LocalModelRegistry` rather than a duplicated list
- `AiSelectionState` returns catalog defaults and honours overrides
- `ImageGenerationRouter` falls back to the `ImageGen:Provider` flag on null and on an unknown id
- `VisionServiceRouter` resolves `ollama:` ids to Ollama and — regression for the collision above —
  resolves `browser:qwen2.5-0.5b-instruct` to Azure, not Ollama

**Integration** (`Tests.Integration`)
- `/api/images/analyze` honours `ImageGenModelId`
- A request carrying `PrecomputedDescription` skips the vision service entirely

**E2E UI** (`Tests.E2E.UI`)
- The six selects render on Studio, and the four single-provider ones are disabled

## Out of scope

- Discovering Ollama's installed models at runtime via `/api/tags`. The Ollama option uses the
  configured `Ollama:VisionModel` (default `gemma4`)
- Persisting selections across reloads
- Adding providers to the four single-provider capabilities
- Per-page pickers — selection stays global on Studio

## Risks

1. **Browser-local execution is the bulk of the work and the risk.** It is a new client execution
   path with failure modes the app has never handled: multi-hundred-megabyte downloads, WebGPU
   absence, worker crashes. The panel and server routing are comparatively mechanical.
2. **First-run download cost is invisible until incurred.** Florence-2 is ~230 MB and Qwen2.5 ~350 MB.
   The option hints must state the size, as the existing picker already does for the vision model.
3. **Four disabled dropdowns is a lot of inert UI.** Accepted deliberately for layout uniformity and
   to document the AI stack; revisit if it reads as broken in use.
