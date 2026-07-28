# Browser-Native Local AI Execution — Design

**Date:** 2026-07-27
**Status:** Approved
**Rule satisfied:** NET_RULES §5 — "Local AI Execution: Implement model registries with dtype fallback chains for browser/worker-native execution."

## Problem

NET_RULES §5 mandates browser/worker-native model execution with dtype fallback chains. The repo has
zero implementation: no references to `dtype`, `webgpu`, `onnx`, or `transformers.js` exist anywhere.
Worse, `AGENT.MD` advertises a "Web Browser — models that run client-side via WebGPU/WebAssembly"
category selectable from the Studio home page, and `Studio.razor` has no model selector at all. Both
the rule and the documentation describe a feature that does not exist.

## Goals

- Run **vision** (image → caption) and **text** (prompt refinement, meme captions) inference in the
  browser, on a worker thread, at zero server cost.
- Implement a genuine **dtype fallback chain** that degrades across quantization variants and devices.
- Make the Studio model-category selector real: **Remote | Web Browser | Ollama**.
- Keep local failures honest — never silently spend cloud money on the user's behalf.

## Non-Goals

- In-browser image *generation* (diffusion). Too heavy for a first increment; generation stays remote.
- Offline-first operation. First run requires network to fetch weights from the HuggingFace CDN.
- Server-side routing for browser models. They never reach the server, so they need no route.

## Prior Art

`github.com/punkouter26/PoLocalCompare` runs WebLLM/WebGPU in the browser and supplies patterns this
design ports directly:

| Pattern | Source | Reused as |
|---|---|---|
| Worker-native execution, `status`/`complete`/`error` postMessage protocol | `wwwroot/js/webllm-worker.js` | Both workers, identical protocol |
| `DotNetObjectReference` + `[JSInvokable]` + session store keyed by model, exposed as `IAsyncEnumerable` | `Services/WebLlmService.cs` | `ILocalInferenceRuntime` implementations |
| Source fallback: local → CDN primary → CDN backup, TTL cache + in-flight dedupe | `resolveBrowserModelAvailability` in `webllm-interop.js` | `local-ai-interop.js`, verbatim |
| Vendored ESM runtime bundle (same-origin, CSP-friendly) | `wwwroot/js/web-llm.js` | Both runtimes vendored |
| `classifyWebLlmError` — device-lost / OOM / no-adapter / shader-f16 → actionable text | `webllm-worker.js` | `LocalAiErrorClassifier` |
| `releaseEngine()` before model switch (reassignment leaks multi-GB VRAM) | `webllm-worker.js` | Preserved in both workers |
| CDN base-URL templates in client `wwwroot/appsettings.json` | `BrowserModels:*CdnBaseUrlTemplate` | Same config keys |

## Runtime Selection

Two runtimes, chosen for capability fit:

- **WebLLM (MLC)** for text — proven in PoLocalCompare, ported with minimal change.
- **transformers.js (ONNX Runtime Web)** for vision — WebLLM's VLM support is narrow, and
  transformers.js exposes `dtype` as a first-class parameter.

The cost of two runtimes is two caches and two bundles. The cost that matters — two *policies* — is
avoided by architecture: the registry, the fallback chain, capability pruning, and advance-on-failure
all live in one C# code path. Runtime adapters only *interpret* a variant.

## Architecture

Client-only. Local inference never crosses the BFF boundary.

```
src/PoRedoImage.Client/LocalAi/
├── LocalModelId.cs            readonly record struct (NET_RULES §1 typed IDs)
├── LocalModelDescriptor.cs    registry entry + LocalCapability / LocalRuntime enums
├── LocalModelRegistry.cs      the single catalog, read by both runtimes
├── DtypeVariant.cs            enum Q4 | Q4F16 | F16 | F32
├── DeviceCapabilities.cs      WebGPU adapter + shader-f16 probe result
├── ILocalInferenceRuntime.cs  common contract
├── WebLlmRuntime.cs           text
├── TransformersRuntime.cs     vision
├── LocalAiErrorClassifier.cs  cryptic GPU/ONNX errors → actionable reasons
└── LocalInferenceStatus.cs    status record

src/PoRedoImage.Client/wwwroot/js/local-ai/
├── local-ai-interop.js        availability probe, worker lifecycle, dtype negotiation
├── webllm-worker.js           text worker      (vendored web-llm.js)
└── transformers-worker.js     vision worker    (vendored transformers.js)
```

Both workers speak an identical postMessage protocol, so `ILocalInferenceRuntime` has one
implementation shape and one C#-side session store. The runtimes differ only inside their worker file.

### Registry entry

```csharp
new LocalModelDescriptor(
    Id: new LocalModelId("florence2-base"),
    DisplayName: "Florence-2 base",
    Capability: LocalCapability.Vision,
    Runtime: LocalRuntime.TransformersJs,
    RepoId: "onnx-community/Florence-2-base",
    VariantChain: [DtypeVariant.Q4, DtypeVariant.F16, DtypeVariant.F32],
    ApproxDownloadMb: 230)
```

`RepoId` is runtime-scoped and the adapter owns its interpretation: for `TransformersJs` it is a
HuggingFace repo path (`onnx-community/Florence-2-base`); for `WebLlm` it is an MLC model id *stem*
without the quantization suffix (`Qwen2.5-0.5B-Instruct`), because the WebLLM adapter appends the
suffix when interpreting the active `DtypeVariant`. The registry never stores a quantization-bearing
id — that would put dtype in two places.

Initial catalog: Florence-2-base (vision, transformers.js, ~230 MB q4) and Qwen2.5-0.5B-Instruct
(text, WebLLM, ~350 MB q4).

## The dtype fallback chain

Two-phase resolution, both phases in C#:

1. **Probe-time pruning.** `DeviceCapabilities` requests a WebGPU adapter and inspects its features.
   - No `shader-f16` → drop `Q4F16` and `F16` from the chain.
   - No WebGPU at all → `device: 'wasm'`, chain pruned to `Q4` and `F32`.
2. **Load-time advance.** On OOM, shader-compile failure, or device-lost: release the engine, advance
   to the next surviving variant, retry. Exhausting the chain is a hard failure carrying the
   classified reason from the last attempt.

Adapters interpret a variant and nothing more:
- transformers.js passes `dtype:` straight through.
- WebLLM rewrites the model-id suffix (`-q4f16_1-MLC` ↔ `-q4f32_1-MLC`).

Because all policy is C# and all I/O is behind the adapter boundary, the entire chain is unit-testable
with no browser and no network.

### Source chain (orthogonal)

Independently of dtype, weights resolve vendored-local → HF primary → HF backup, with a TTL cache and
in-flight dedupe, driven by `BrowserModels:PrimaryCdnBaseUrlTemplate` and
`BrowserModels:BackupCdnBaseUrlTemplate` in the client's `wwwroot/appsettings.json`. Ported verbatim.

## Integration

`Studio.razor` gains the model-category selector it never had: **Remote | Web Browser | Ollama**.
Selection persists in the existing `ImageSessionService` and flows through the existing `ModelId` field
on `ImageAnalysisRequest`.

When **Web Browser** is active:
- Florence-2 captions the image in the browser; the client posts the **caption**, not the image bytes,
  to the BFF. This is a deliberate contract change for the local path — it is the source of the
  bandwidth and cost saving, and it means the image never leaves the device for the analysis step.
- Qwen2.5-0.5B handles prompt refinement and meme caption writing locally.
- Image **generation** remains remote and unchanged.

`VisionServiceRouter` is untouched. Browser models never reach the server.

## Error handling

`LocalAiErrorClassifier` ports PoLocalCompare's taxonomy (device-lost, OOM, no-adapter, shader-f16,
network, model-lib) and extends it for ONNX/wasm faults. `releaseEngine()`-before-switch is preserved
in both workers.

**On local failure the UI surfaces the classified reason plus an explicit "Switch to Remote" action.
It does not silently fall back to cloud.** Silent fallback would spend the user's money without
consent, and the premise of local mode is that it is free.

## Testing

NET_RULES §6 counts are enforced as per-tier **ceilings** by `TestCountCeilingTests`. Headroom at time
of writing: Unit 15, Integration 9, E2E API 14, E2E UI 18.

- **Unit (~10):** chain pruning under each capability profile (no-WebGPU, WebGPU without shader-f16,
  full WebGPU), advance-on-failure, chain exhaustion, registry integrity, `LocalModelId` validation.
  Pure logic, no I/O — the design puts all policy in C# specifically to make this possible.
- **E2E UI (~3):** guarded by a new `[LocalAiFact]` that self-skips when WebGPU is unavailable,
  mirroring the existing `[LiveServerFact]`. Headless Chromium WebGPU is unreliable in CI, so
  self-skipping is mandatory rather than optional.
- **Integration: none.** Nothing crosses the server boundary.

## Consequences

- CSP `connect-src` must allow `huggingface.co` and `cdn-lfs.huggingface.co` for weight fetches.
- Two vendored ESM bundles are added to `wwwroot`, increasing static asset count but not the .NET
  publish output or B1 egress for weights (which stream from the CDN).
- First local run requires network. Subsequent runs hit the browser Cache API.
- `AGENT.MD`'s claim of three Studio categories becomes true rather than aspirational.
