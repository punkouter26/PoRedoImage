# Per-capability AI Service Pickers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the decorative three-way `ModelCategoryPicker` on Studio with one dropdown per AI capability, and make the selections actually route work — including to browser-local models.

**Architecture:** A client-side catalog (`AiServiceCatalog`) enumerates providers per capability; a scoped `AiSelectionState` holds the session's choices; selections travel to the BFF as namespaced ids on `ImageAnalysisRequest`. Server-side routers (`VisionServiceRouter`, new `ImageGenerationRouter`) resolve those ids to concrete services. Browser-local models execute in the client via the existing `LocalAiService` and post their output in new precomputed fields, letting the orchestrator skip its vision step.

**Tech Stack:** .NET 10, C# 14, Blazor WASM (global InteractiveWebAssembly, no prerender), Radzen + Bootstrap, xUnit + Moq, Playwright, Testcontainers.

**Source spec:** `docs/superpowers/specs/2026-07-28-ai-service-pickers-design.md`

## Global Constraints

- **.NET 10**, pinned via `global.json`. `<LangVersion>latest</LangVersion>` (C# 14).
- **`TreatWarningsAsErrors`** and **`Nullable`** are enabled solution-wide. A warning fails the build.
- **Central Package Management** — `Directory.Packages.props` is the only place versions live. **This plan adds no new packages.**
- **Zero-Waste Policy** — delete replaced files in the same commit that replaces them.
- **VSA wins over Onion.** Server features live in `src/PoRedoImage.Web/Features/{Name}/`. `Domain`/`Application`/`Infrastructure` hold cross-slice primitives only.
- **All interactive components live in `src/PoRedoImage.Client/`.** Never set `RenderMode.InteractiveServer`.
- **BFF invariant** — no tokens in WASM. This plan sends only model ids, never credentials.
- **`.Shared` must be trim-safe** (`EnableTrimAnalyzer`). Use plain records, const strings, and explicit properties — no reflection-driven binding.
- **Test tier ceilings (100/50/25)** are enforced by `TestCountCeilingTests` in each tier. **The Unit tier is close to its 100-method ceiling.** After adding unit tests, run the guardrail; if it fails, move the router tests to the Integration tier rather than deleting coverage.
- **Build and test commands:**
  - `dotnet build PoRedoImage.slnx`
  - `dotnet test tests/PoRedoImage.Tests.Unit`
  - `dotnet test tests/PoRedoImage.Tests.Integration`
  - **Stop the dev server before building or testing** — a running `PoRedoImage.Web` locks the output DLLs and the build fails with MSB3027.

---

## File Structure

**Created**

| File | Responsibility |
|---|---|
| `src/PoRedoImage.Shared/Configuration/AiProviderIds.cs` | The namespaced id vocabulary shared by client catalog and server routers |
| `src/PoRedoImage.Domain/Interfaces/IImageGenerationRouter.cs` | Per-request image-generation provider selection |
| `src/PoRedoImage.Infrastructure/Services/ImageGenerationRouter.cs` | `ImageGenerationRouter` + `SingleImageGenerationRouter` |
| `src/PoRedoImage.Client/Models/AiCapability.cs` | The six capability values |
| `src/PoRedoImage.Client/Models/AiProviderOption.cs` | One selectable provider |
| `src/PoRedoImage.Client/Models/AiServiceCatalog.cs` | Capability → options |
| `src/PoRedoImage.Client/Models/AiSelectionState.cs` | Scoped session selection state |
| `src/PoRedoImage.Client/Shared/AiServicePicker.razor` | The six-dropdown panel |
| `src/PoRedoImage.Client/Shared/AiServicePicker.razor.css` | Panel styles |

**Modified**

| File | Change |
|---|---|
| `src/PoRedoImage.Infrastructure/Services/VisionServiceRouter.cs` | Explicit `ollama:` match replacing prefix-guessing |
| `src/PoRedoImage.Infrastructure/InfrastructureServiceExtensions.cs` | Register both routers in the mock and real branches |
| `src/PoRedoImage.Application/Features/ImageAnalysis/ImageAnalysisOrchestrator.cs` | Take `IImageGenerationRouter`; add precomputed-vision branch |
| `src/PoRedoImage.Shared/DTOs/ImageAnalysisRequest.cs` | Add `ImageGenModelId`, `PrecomputedDescription`, `PrecomputedTags` |
| `src/PoRedoImage.Client/Program.cs` | Register `AiSelectionState` |
| `src/PoRedoImage.Client/Pages/Studio.razor` | Swap picker component; drop `_modelCategory` |
| `src/PoRedoImage.Client/Pages/FeaturePageBase.cs` | Inject selection state + `LocalAiService`; build requests; run local inference |
| `src/PoRedoImage.Client/Pages/ImageRegeneration.razor` | Use the base-class request builder |
| `src/PoRedoImage.Client/Pages/MemeGeneration.razor` | Use the base-class request builder |

**Deleted**

- `src/PoRedoImage.Client/Shared/ModelCategoryPicker.razor`
- `src/PoRedoImage.Client/Shared/ModelCategoryPicker.razor.css`

---

## Task 1: Namespaced provider ids and router tightening

Fixes the collision where `qwen2.5-0.5b-instruct` (a browser model) is routed to Ollama by
`VisionServiceRouter`'s prefix matching.

**Files:**
- Create: `src/PoRedoImage.Shared/Configuration/AiProviderIds.cs`
- Modify: `src/PoRedoImage.Infrastructure/Services/VisionServiceRouter.cs`
- Test: `tests/PoRedoImage.Tests.Unit/Features/VisionServiceRouterTests.cs`

**Interfaces:**
- Consumes: nothing (first task)
- Produces: `AiProviderIds` static class with const string ids and `IsOllama(string?)` / `IsBrowser(string?)` predicates. Tasks 2, 4, 5, 6 all reference these constants.

- [ ] **Step 1: Write the failing test**

Create `tests/PoRedoImage.Tests.Unit/Features/VisionServiceRouterTests.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using PoRedoImage.Infrastructure.Services;
using PoRedoImage.Shared.Configuration;

namespace PoRedoImage.Tests.Unit.Features;

/// <summary>
/// Guards the id-namespacing contract. The original router matched by bare prefix, so the browser
/// text model "qwen2.5-0.5b-instruct" resolved to Ollama — work would have gone to the wrong
/// backend the moment a browser id reached the server.
/// </summary>
public class VisionServiceRouterTests
{
    private static VisionServiceRouter BuildRouter(out AzureVisionService azure, out OllamaVisionService ollama)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ComputerVision:Endpoint"] = "https://test.cognitiveservices.azure.com/",
            ["ComputerVision:ApiKey"] = "test-key",
            ["Ollama:Endpoint"] = "http://localhost:11434",
        }).Build();

        azure = new AzureVisionService(config, Mock.Of<ILogger<AzureVisionService>>());

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());
        ollama = new OllamaVisionService(factory.Object, config, Mock.Of<ILogger<OllamaVisionService>>());

        return new VisionServiceRouter(azure, ollama);
    }

    [Fact]
    public void Resolve_OllamaNamespacedId_ReturnsOllama()
    {
        var router = BuildRouter(out _, out var ollama);
        Assert.Same(ollama, router.Resolve(AiProviderIds.OllamaVision));
    }

    [Fact]
    public void Resolve_BrowserQwenId_ReturnsAzure_NotOllama()
    {
        // Regression: "qwen..." previously matched the Ollama prefix rule.
        var router = BuildRouter(out var azure, out _);
        Assert.Same(azure, router.Resolve(AiProviderIds.BrowserQwen25));
    }

    [Fact]
    public void Resolve_NullOrUnnamespacedId_ReturnsAzure()
    {
        var router = BuildRouter(out var azure, out _);
        Assert.Same(azure, router.Resolve(null));
        Assert.Same(azure, router.Resolve("gemma4"));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```powershell
dotnet test tests/PoRedoImage.Tests.Unit --filter "FullyQualifiedName~VisionServiceRouterTests"
```

Expected: FAIL — `AiProviderIds` does not exist (compile error CS0246).

- [ ] **Step 3: Create the id vocabulary**

Create `src/PoRedoImage.Shared/Configuration/AiProviderIds.cs`:

```csharp
namespace PoRedoImage.Shared.Configuration;

/// <summary>
/// The namespaced provider-id vocabulary shared by the client catalog and the server routers.
/// </summary>
/// <remarks>
/// Ids are namespaced by execution location — <c>remote:</c>, <c>ollama:</c>, <c>browser:</c> —
/// because the previous scheme matched bare model-name prefixes and could not tell a browser model
/// apart from an Ollama one. Const strings only: this type is consumed by the trim-analysed
/// <c>.Shared</c> assembly.
/// </remarks>
public static class AiProviderIds
{
    public const string RemotePrefix = "remote:";
    public const string OllamaPrefix = "ollama:";
    public const string BrowserPrefix = "browser:";

    // Remote (hosted APIs)
    public const string AzureComputerVision = "remote:azure-cv";
    public const string AzureOpenAi = "remote:azure-openai";
    public const string GeminiImagen3 = "remote:gemini-imagen3";
    public const string HuggingFaceFlux = "remote:hf-flux-schnell";
    public const string HuggingFaceChat = "remote:hf-chat";
    public const string GoogleLyria = "remote:google-lyria";

    // Ollama (local service, dev only)
    public const string OllamaVision = "ollama:vision";

    // Browser (WebGPU / WebAssembly, executed client-side)
    public const string BrowserFlorence2 = "browser:florence2-base";
    public const string BrowserQwen25 = "browser:qwen2.5-0.5b-instruct";

    /// <summary>True when the id names the local Ollama service.</summary>
    public static bool IsOllama(string? id) =>
        id?.StartsWith(OllamaPrefix, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>True when the id names a model that executes in the browser.</summary>
    public static bool IsBrowser(string? id) =>
        id?.StartsWith(BrowserPrefix, StringComparison.OrdinalIgnoreCase) == true;
}
```

- [ ] **Step 4: Tighten the router**

Replace the body of `src/PoRedoImage.Infrastructure/Services/VisionServiceRouter.cs`:

```csharp
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Shared.Configuration;

namespace PoRedoImage.Infrastructure.Services;

/// <summary>
/// Routes the vision/analysis step to the local Ollama service when the selected id is
/// <c>ollama:</c>-namespaced, otherwise to the default Azure Computer Vision backend.
/// </summary>
/// <remarks>
/// Matching is an explicit namespace check, not a model-name prefix guess. The previous rule
/// treated any id starting with "qwen" as Ollama, which collides with the browser text model
/// <c>browser:qwen2.5-0.5b-instruct</c>. Browser ids resolve to Azure here because a browser
/// selection is executed client-side and should never have reached the server at all — falling back
/// to the default backend is the safe reading of an id this router should not have seen.
/// </remarks>
public sealed class VisionServiceRouter(AzureVisionService azure, OllamaVisionService ollama) : IVisionServiceRouter
{
    public IVisionService Resolve(string? modelId) =>
        AiProviderIds.IsOllama(modelId) ? ollama : azure;
}

/// <summary>
/// Router used when a single vision service should handle every request (e.g. mock mode).
/// </summary>
public sealed class SingleVisionServiceRouter(IVisionService service) : IVisionServiceRouter
{
    public IVisionService Resolve(string? modelId) => service;
}
```

- [ ] **Step 5: Run the tests to verify they pass**

```powershell
dotnet test tests/PoRedoImage.Tests.Unit --filter "FullyQualifiedName~VisionServiceRouterTests"
```

Expected: PASS — 3 passed.

- [ ] **Step 6: Verify the tier ceiling still holds**

```powershell
dotnet test tests/PoRedoImage.Tests.Unit --filter "FullyQualifiedName~TestCountCeiling"
```

Expected: PASS. If it FAILS with "exceeding the ceiling of 100", move `VisionServiceRouterTests` to `tests/PoRedoImage.Tests.Integration/` and rerun both tiers.

- [ ] **Step 7: Commit**

```bash
git add src/PoRedoImage.Shared/Configuration/AiProviderIds.cs \
        src/PoRedoImage.Infrastructure/Services/VisionServiceRouter.cs \
        tests/PoRedoImage.Tests.Unit/Features/VisionServiceRouterTests.cs
git commit -m "fix(ai): namespace provider ids and stop prefix-guessing in VisionServiceRouter"
```

---

## Task 2: Per-request image-generation routing

`IImageGenerationService` is currently bound to one singleton at startup by the `ImageGen:Provider`
flag. This adds a router so the client can choose per request, while preserving the flag as the
fallback.

**Files:**
- Create: `src/PoRedoImage.Domain/Interfaces/IImageGenerationRouter.cs`
- Create: `src/PoRedoImage.Infrastructure/Services/ImageGenerationRouter.cs`
- Modify: `src/PoRedoImage.Infrastructure/InfrastructureServiceExtensions.cs` (mock branch ~line 36-66; real branch ~line 82-89)
- Modify: `src/PoRedoImage.Application/Features/ImageAnalysis/ImageAnalysisOrchestrator.cs`
- Test: `tests/PoRedoImage.Tests.Unit/Features/ImageGenerationRouterTests.cs`

**Interfaces:**
- Consumes: `AiProviderIds.GeminiImagen3`, `AiProviderIds.HuggingFaceFlux` (Task 1)
- Produces: `IImageGenerationRouter.Resolve(string? modelId) → IImageGenerationService`. Task 3 injects this into the orchestrator.

- [ ] **Step 1: Write the failing test**

Create `tests/PoRedoImage.Tests.Unit/Features/ImageGenerationRouterTests.cs`:

```csharp
using Moq;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Infrastructure.Services;
using PoRedoImage.Shared.Configuration;

namespace PoRedoImage.Tests.Unit.Features;

/// <summary>
/// The router must never widen behaviour for callers that send nothing: a null or unrecognised id
/// has to resolve exactly as the ImageGen:Provider flag already did, or flipping the flag in config
/// would silently stop working.
/// </summary>
public class ImageGenerationRouterTests
{
    private static readonly IImageGenerationService Gemini = Mock.Of<IImageGenerationService>();
    private static readonly IImageGenerationService HuggingFace = Mock.Of<IImageGenerationService>();

    private static ImageGenerationRouter Build(string configuredDefault) =>
        new(Gemini, HuggingFace, configuredDefault);

    [Fact]
    public void Resolve_HuggingFaceId_ReturnsHuggingFace()
    {
        Assert.Same(HuggingFace, Build("google").Resolve(AiProviderIds.HuggingFaceFlux));
    }

    [Fact]
    public void Resolve_GeminiId_ReturnsGemini()
    {
        Assert.Same(Gemini, Build("huggingface").Resolve(AiProviderIds.GeminiImagen3));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("remote:something-unknown")]
    public void Resolve_NullOrUnknownId_FallsBackToConfiguredProvider(string? modelId)
    {
        Assert.Same(HuggingFace, Build("huggingface").Resolve(modelId));
        Assert.Same(Gemini, Build("google").Resolve(modelId));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```powershell
dotnet test tests/PoRedoImage.Tests.Unit --filter "FullyQualifiedName~ImageGenerationRouterTests"
```

Expected: FAIL — `ImageGenerationRouter` does not exist (CS0246).

- [ ] **Step 3: Create the interface**

Create `src/PoRedoImage.Domain/Interfaces/IImageGenerationRouter.cs`:

```csharp
namespace PoRedoImage.Domain.Interfaces;

/// <summary>
/// Selects the <see cref="IImageGenerationService"/> backend for a requested provider id.
/// Strategy pattern (GoF), mirroring <see cref="IVisionServiceRouter"/>.
/// </summary>
public interface IImageGenerationRouter
{
    /// <summary>
    /// Returns the image-generation service for the given id. Null or unrecognised ids fall back to
    /// the provider named by the <c>ImageGen:Provider</c> configuration flag.
    /// </summary>
    IImageGenerationService Resolve(string? modelId);
}
```

- [ ] **Step 4: Create the implementations**

Create `src/PoRedoImage.Infrastructure/Services/ImageGenerationRouter.cs`:

```csharp
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Shared.Configuration;

namespace PoRedoImage.Infrastructure.Services;

/// <summary>
/// Routes image generation to Gemini/Imagen or HuggingFace per request.
/// </summary>
/// <remarks>
/// The <c>ImageGen:Provider</c> flag remains the fallback rather than being replaced, so a request
/// that carries no id behaves exactly as it did before per-request routing existed. That keeps the
/// no-redeploy config flip working and makes this change additive.
/// </remarks>
public sealed class ImageGenerationRouter(
    IImageGenerationService gemini,
    IImageGenerationService huggingFace,
    string configuredProvider) : IImageGenerationRouter
{
    private readonly bool _defaultIsHuggingFace =
        configuredProvider is "huggingface" or "hf";

    public IImageGenerationService Resolve(string? modelId) => modelId switch
    {
        AiProviderIds.HuggingFaceFlux => huggingFace,
        AiProviderIds.GeminiImagen3 => gemini,
        _ => _defaultIsHuggingFace ? huggingFace : gemini,
    };
}

/// <summary>
/// Router used when a single generation service should handle every request (e.g. mock mode).
/// </summary>
public sealed class SingleImageGenerationRouter(IImageGenerationService service) : IImageGenerationRouter
{
    public IImageGenerationService Resolve(string? modelId) => service;
}
```

- [ ] **Step 5: Run the tests to verify they pass**

```powershell
dotnet test tests/PoRedoImage.Tests.Unit --filter "FullyQualifiedName~ImageGenerationRouterTests"
```

Expected: PASS — 5 passed (3 theory cases + 2 facts).

- [ ] **Step 6: Register both routers in DI**

In `src/PoRedoImage.Infrastructure/InfrastructureServiceExtensions.cs`, inside the **mock branch**
(`if (useMockAi)`), immediately after the existing `MockImagen3Service` registration, add:

```csharp
            services.AddSingleton<IImageGenerationRouter>(sp =>
                new SingleImageGenerationRouter(sp.GetRequiredService<IImageGenerationService>()));
```

In the **real branch** (`else`), replace the existing `IImageGenerationService` registration block
with:

```csharp
            services.AddSingleton<GeminiImagen3Service>();
            services.AddSingleton<HuggingFaceImageGenerationService>();
            var imageProvider = (configuration?[ConfigKeys.ImageGenProvider] ?? "google").Trim().ToLowerInvariant();

            // Kept so callers that resolve the interface directly (health checks, other slices)
            // still get the configured default.
            services.AddSingleton<IImageGenerationService>(sp => imageProvider switch
            {
                "huggingface" or "hf" => sp.GetRequiredService<HuggingFaceImageGenerationService>(),
                _ => sp.GetRequiredService<GeminiImagen3Service>()
            });

            services.AddSingleton<IImageGenerationRouter>(sp => new ImageGenerationRouter(
                sp.GetRequiredService<GeminiImagen3Service>(),
                sp.GetRequiredService<HuggingFaceImageGenerationService>(),
                imageProvider));
```

- [ ] **Step 7: Switch the orchestrator to the router**

In `src/PoRedoImage.Application/Features/ImageAnalysis/ImageAnalysisOrchestrator.cs`, change the
primary constructor parameter `IImageGenerationService imagen3Service` to
`IImageGenerationRouter imageGenRouter`, and replace both uses inside `ProcessAsync`:

```csharp
            var imageGenService = imageGenRouter.Resolve(request.ImageGenModelId);

            if (!imageGenService.IsConfigured)
            {
                throw new InvalidOperationException(
                    "Image generation is not configured. Set the Gemini API key (Google:ApiKey) via Key Vault or appsettings.");
            }

            var (imgData, imgType, regenMs) = await imageGenService.GenerateAsync(enhanced, ct);
```

> `request.ImageGenModelId` does not exist yet — it is added in Task 3. Build will fail until then;
> that is expected and is why Steps 8-9 build only after Task 3. Proceed to Step 8.

- [ ] **Step 8: Commit**

```bash
git add src/PoRedoImage.Domain/Interfaces/IImageGenerationRouter.cs \
        src/PoRedoImage.Infrastructure/Services/ImageGenerationRouter.cs \
        src/PoRedoImage.Infrastructure/InfrastructureServiceExtensions.cs \
        src/PoRedoImage.Application/Features/ImageAnalysis/ImageAnalysisOrchestrator.cs \
        tests/PoRedoImage.Tests.Unit/Features/ImageGenerationRouterTests.cs
git commit -m "feat(ai): add per-request image generation routing"
```

---

## Task 3: Request DTO fields and the precomputed-vision branch

Adds the three fields the client needs and lets the orchestrator skip its vision step when the
browser already produced a description. Completes the build broken at the end of Task 2.

**Files:**
- Modify: `src/PoRedoImage.Shared/DTOs/ImageAnalysisRequest.cs`
- Modify: `src/PoRedoImage.Application/Features/ImageAnalysis/ImageAnalysisOrchestrator.cs:24-31`
- Test: `tests/PoRedoImage.Tests.Integration/Features/PrecomputedVisionTests.cs`

**Interfaces:**
- Consumes: `IImageGenerationRouter` (Task 2)
- Produces: `ImageAnalysisRequest.ImageGenModelId`, `.PrecomputedDescription`, `.PrecomputedTags`. Tasks 6 and 7 populate these.

- [ ] **Step 1: Add the DTO fields**

Append to `src/PoRedoImage.Shared/DTOs/ImageAnalysisRequest.cs`, inside the class after `ModelId`:

```csharp
    /// <summary>
    /// Optional selected image-generation provider id (see <c>AiProviderIds</c>). Null falls back to
    /// the provider named by the <c>ImageGen:Provider</c> flag.
    /// </summary>
    public string? ImageGenModelId { get; set; }

    /// <summary>
    /// Description already produced by a browser-local vision model. When set, the server skips its
    /// own vision step and uses this instead.
    /// </summary>
    public string? PrecomputedDescription { get; set; }

    /// <summary>
    /// Tags accompanying <see cref="PrecomputedDescription"/>. Ignored unless that is also set.
    /// </summary>
    public IReadOnlyList<string>? PrecomputedTags { get; set; }
```

Also update the `ModelId` doc comment to point at the new vocabulary:

```csharp
    /// <summary>
    /// Optional selected vision provider id (see <c>AiProviderIds</c>, e.g. "ollama:vision").
    /// Null or unrecognised ids fall back to the default Azure vision service.
    /// </summary>
    public string? ModelId { get; set; }
```

- [ ] **Step 2: Write the failing integration test**

Create `tests/PoRedoImage.Tests.Integration/Features/PrecomputedVisionTests.cs`. Follow the existing
fixture pattern in that project — read `tests/PoRedoImage.Tests.Integration/AzuriteContainerFixture.cs`
and one neighbouring test class first to match how the `WebApplicationFactory` and fake auth headers
are set up, then write:

```csharp
using System.Net;
using System.Net.Http.Json;
using PoRedoImage.Shared.DTOs;

namespace PoRedoImage.Tests.Integration.Features;

/// <summary>
/// When the browser has already run vision locally, the server must not run it again — otherwise a
/// user who chose a free on-device model still pays for a metered Azure call.
/// </summary>
public class PrecomputedVisionTests(PoRedoImageWebFactory factory) : IClassFixture<PoRedoImageWebFactory>
{
    [Fact]
    public async Task Analyze_WithPrecomputedDescription_SkipsVisionAndReportsZeroAnalysisTime()
    {
        var client = factory.CreateAuthenticatedClient();

        var request = new ImageAnalysisRequest
        {
            ImageData = TestImages.OnePixelPngBase64,
            ContentType = "image/png",
            FileName = "test.png",
            Mode = ProcessingMode.MemeGeneration,
            PrecomputedDescription = "a lighthouse at dusk",
            PrecomputedTags = ["lighthouse", "dusk"],
        };

        var response = await client.PostAsJsonAsync("/api/images/analyze", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ImageAnalysisResponse>();
        Assert.NotNull(body);
        Assert.Equal(0, body.Metrics!.ImageAnalysisTimeMs);
        Assert.Contains("lighthouse", body.Tags);
    }
}
```

> Replace `PoRedoImageWebFactory`, `CreateAuthenticatedClient()`, and `TestImages.OnePixelPngBase64`
> with the actual fixture type, auth helper, and image constant used by the neighbouring tests in
> `tests/PoRedoImage.Tests.Integration/`. Do not invent new helpers if equivalents already exist.

- [ ] **Step 3: Run the test to verify it fails**

```powershell
dotnet test tests/PoRedoImage.Tests.Integration --filter "FullyQualifiedName~PrecomputedVisionTests"
```

Expected: FAIL — `ImageAnalysisTimeMs` is non-zero because the vision service still runs.

- [ ] **Step 4: Add the orchestrator branch**

In `src/PoRedoImage.Application/Features/ImageAnalysis/ImageAnalysisOrchestrator.cs`, replace the
Step 1 block (currently lines 24-31):

```csharp
        // Step 1 — Vision analysis. Skipped entirely when the client ran a browser-local model and
        // supplied the result: re-running it would bill a metered API for work already done for free.
        string description;
        IReadOnlyList<string> tags;
        double confidence;

        if (!string.IsNullOrWhiteSpace(request.PrecomputedDescription))
        {
            description = request.PrecomputedDescription;
            tags = request.PrecomputedTags ?? [];
            // Local models emit no calibrated confidence; report 1.0 so downstream gating treats the
            // result as usable, matching how OllamaVisionService already handles this.
            confidence = 1.0;
            metrics.ImageAnalysisTimeMs = 0;
        }
        else
        {
            var visionService = visionRouter.Resolve(request.ModelId);
            var (visionDescription, visionTags, visionConfidence, analysisMs) =
                await visionService.AnalyzeAsync(imageBytes, ct);
            description = visionDescription;
            tags = visionTags;
            confidence = visionConfidence;
            metrics.ImageAnalysisTimeMs = analysisMs;
        }

        response.Tags = [.. tags];
        response.ConfidenceScore = confidence;
```

- [ ] **Step 5: Build the solution**

```powershell
dotnet build PoRedoImage.slnx
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` — this also clears the Task 2 break.

- [ ] **Step 6: Run the test to verify it passes**

```powershell
dotnet test tests/PoRedoImage.Tests.Integration --filter "FullyQualifiedName~PrecomputedVisionTests"
```

Expected: PASS.

- [ ] **Step 7: Run both server tiers for regressions**

```powershell
dotnet test tests/PoRedoImage.Tests.Unit
dotnet test tests/PoRedoImage.Tests.Integration
```

Expected: all PASS, including the ceiling guardrails.

- [ ] **Step 8: Commit**

```bash
git add src/PoRedoImage.Shared/DTOs/ImageAnalysisRequest.cs \
        src/PoRedoImage.Application/Features/ImageAnalysis/ImageAnalysisOrchestrator.cs \
        tests/PoRedoImage.Tests.Integration/Features/PrecomputedVisionTests.cs
git commit -m "feat(ai): accept precomputed vision output and skip the server vision step"
```

---

## Task 4: Client catalog and selection state

**Files:**
- Create: `src/PoRedoImage.Client/Models/AiCapability.cs`
- Create: `src/PoRedoImage.Client/Models/AiProviderOption.cs`
- Create: `src/PoRedoImage.Client/Models/AiServiceCatalog.cs`
- Create: `src/PoRedoImage.Client/Models/AiSelectionState.cs`
- Modify: `src/PoRedoImage.Client/Program.cs`
- Test: `tests/PoRedoImage.Tests.Unit/Features/AiServiceCatalogTests.cs`

**Interfaces:**
- Consumes: `AiProviderIds` (Task 1), `LocalModelRegistry` (existing)
- Produces: `AiCapability` enum; `AiProviderOption(string Id, string DisplayName, string Category, string Hint, bool ExecutesInBrowser)`; `AiServiceCatalog.OptionsFor(AiCapability)`, `.DefaultFor(AiCapability)`, `.All`; `AiSelectionState.Get(AiCapability)`, `.Set(AiCapability, string)`, `.OnChange` event. Tasks 5, 6, 7 consume these.

- [ ] **Step 1: Write the failing test**

Create `tests/PoRedoImage.Tests.Unit/Features/AiServiceCatalogTests.cs`:

```csharp
using PoRedoImage.Client.LocalAi;
using PoRedoImage.Client.Models;
using PoRedoImage.Shared.Configuration;

namespace PoRedoImage.Tests.Unit.Features;

/// <summary>
/// The catalog is the single source of truth the picker renders from. Browser options must be
/// derived from LocalModelRegistry rather than restated, or the two lists drift.
/// </summary>
public class AiServiceCatalogTests
{
    [Fact]
    public void AnalyzeImage_OffersRemoteOllamaAndBrowser()
    {
        var ids = AiServiceCatalog.OptionsFor(AiCapability.AnalyzeImage).Select(o => o.Id).ToList();

        Assert.Contains(AiProviderIds.AzureComputerVision, ids);
        Assert.Contains(AiProviderIds.OllamaVision, ids);
        Assert.Contains(AiProviderIds.BrowserFlorence2, ids);
    }

    [Fact]
    public void SingleProviderCapabilities_HaveExactlyOneOption()
    {
        Assert.Single(AiServiceCatalog.OptionsFor(AiCapability.StyleDirector));
        Assert.Single(AiServiceCatalog.OptionsFor(AiCapability.SceneDetail));
        Assert.Single(AiServiceCatalog.OptionsFor(AiCapability.CreateAudio));
    }

    [Fact]
    public void BrowserOptions_MirrorTheLocalModelRegistry()
    {
        var florence = LocalModelRegistry.DefaultFor(LocalCapability.Vision);
        Assert.NotNull(florence);

        var option = AiServiceCatalog.OptionsFor(AiCapability.AnalyzeImage)
            .Single(o => o.Id == AiProviderIds.BrowserFlorence2);

        Assert.True(option.ExecutesInBrowser);
        Assert.Contains(florence.DisplayName, option.DisplayName);
        Assert.Contains($"{florence.ApproxDownloadMb} MB", option.Hint);
    }

    [Fact]
    public void SelectionState_ReturnsCatalogDefaultUntilOverridden()
    {
        var state = new AiSelectionState();

        Assert.Equal(AiProviderIds.AzureComputerVision, state.Get(AiCapability.AnalyzeImage));

        state.Set(AiCapability.AnalyzeImage, AiProviderIds.BrowserFlorence2);
        Assert.Equal(AiProviderIds.BrowserFlorence2, state.Get(AiCapability.AnalyzeImage));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```powershell
dotnet test tests/PoRedoImage.Tests.Unit --filter "FullyQualifiedName~AiServiceCatalogTests"
```

Expected: FAIL — `AiCapability` / `AiServiceCatalog` / `AiSelectionState` do not exist (CS0246).

- [ ] **Step 3: Create the capability enum**

Create `src/PoRedoImage.Client/Models/AiCapability.cs`:

```csharp
namespace PoRedoImage.Client.Models;

/// <summary>
/// A distinct AI job the app performs. One selector is rendered per value.
/// </summary>
public enum AiCapability
{
    /// <summary>Image in, description + tags out (<c>IVisionService</c>).</summary>
    AnalyzeImage = 0,

    /// <summary>Prompt in, image out (<c>IImageGenerationService</c>).</summary>
    GenerateImage = 1,

    /// <summary>
    /// Description enhancement and meme captions (<c>IGenerativeAiService</c>). Both methods share
    /// one implementation and one provider, so they share one selector.
    /// </summary>
    EnhanceDescription = 2,

    /// <summary>Style Director reasoning agents (<c>IChatCompletionService</c>).</summary>
    StyleDirector = 3,

    /// <summary>OCR, dense captions, objects (<c>ISceneDetailProvider</c>).</summary>
    SceneDetail = 4,

    /// <summary>Lyrics in, performed track out (<c>IMusicGenerationService</c>).</summary>
    CreateAudio = 5,
}
```

- [ ] **Step 4: Create the option record**

Create `src/PoRedoImage.Client/Models/AiProviderOption.cs`:

```csharp
namespace PoRedoImage.Client.Models;

/// <summary>
/// One selectable provider for a capability.
/// </summary>
/// <param name="Id">Namespaced id from <c>AiProviderIds</c>; sent to the BFF.</param>
/// <param name="DisplayName">Label shown in the dropdown.</param>
/// <param name="Category">Optgroup heading — "Remote", "Web Browser", or "Ollama".</param>
/// <param name="Hint">Short qualifier shown after the name, e.g. download size or cost.</param>
/// <param name="ExecutesInBrowser">
/// When true the client runs this model itself and posts the result instead of asking the server.
/// </param>
public sealed record AiProviderOption(
    string Id,
    string DisplayName,
    string Category,
    string Hint,
    bool ExecutesInBrowser = false);
```

- [ ] **Step 5: Create the catalog**

Create `src/PoRedoImage.Client/Models/AiServiceCatalog.cs`:

```csharp
using PoRedoImage.Client.LocalAi;
using PoRedoImage.Shared.Configuration;

namespace PoRedoImage.Client.Models;

/// <summary>
/// The provider options offered per capability — the single source the picker renders from.
/// </summary>
/// <remarks>
/// Browser entries are derived from <see cref="LocalModelRegistry"/> rather than restated, so that
/// registry remains the one catalog of browser models (NET_RULES §5) and download sizes cannot drift
/// between the two.
/// </remarks>
public static class AiServiceCatalog
{
    public const string CategoryRemote = "Remote";
    public const string CategoryBrowser = "Web Browser";
    public const string CategoryOllama = "Ollama";

    private static AiProviderOption BrowserOption(string id, LocalCapability capability)
    {
        var model = LocalModelRegistry.DefaultFor(capability)
            ?? throw new InvalidOperationException($"No local model registered for {capability}.");

        return new AiProviderOption(
            id,
            model.DisplayName,
            CategoryBrowser,
            $"~{model.ApproxDownloadMb} MB first run, then free",
            ExecutesInBrowser: true);
    }

    private static readonly Dictionary<AiCapability, IReadOnlyList<AiProviderOption>> Catalog = new()
    {
        [AiCapability.AnalyzeImage] =
        [
            new(AiProviderIds.AzureComputerVision, "Azure Computer Vision", CategoryRemote, "Fastest, uses your API quota"),
            BrowserOption(AiProviderIds.BrowserFlorence2, LocalCapability.Vision),
            new(AiProviderIds.OllamaVision, "Ollama", CategoryOllama, "Local service, dev only"),
        ],

        [AiCapability.GenerateImage] =
        [
            new(AiProviderIds.GeminiImagen3, "Gemini Imagen 3", CategoryRemote, "Default provider"),
            new(AiProviderIds.HuggingFaceFlux, "FLUX.1-schnell", CategoryRemote, "HuggingFace, ~$0.003/image"),
        ],

        [AiCapability.EnhanceDescription] =
        [
            new(AiProviderIds.AzureOpenAi, "Azure OpenAI", CategoryRemote, "Fastest, uses your API quota"),
            BrowserOption(AiProviderIds.BrowserQwen25, LocalCapability.Text),
        ],

        [AiCapability.StyleDirector] =
        [
            new(AiProviderIds.HuggingFaceChat, "HuggingFace chat", CategoryRemote, "Only provider configured"),
        ],

        [AiCapability.SceneDetail] =
        [
            new(AiProviderIds.AzureComputerVision, "Azure Computer Vision", CategoryRemote, "Only provider configured"),
        ],

        [AiCapability.CreateAudio] =
        [
            new(AiProviderIds.GoogleLyria, "Google Lyria 3", CategoryRemote, "Only provider configured"),
        ],
    };

    /// <summary>Human label for a capability, used as the row heading.</summary>
    public static string LabelFor(AiCapability capability) => capability switch
    {
        AiCapability.AnalyzeImage => "Analyze image",
        AiCapability.GenerateImage => "Generate image",
        AiCapability.EnhanceDescription => "Enhance description & captions",
        AiCapability.StyleDirector => "Style Director",
        AiCapability.SceneDetail => "Scene detail (OCR)",
        AiCapability.CreateAudio => "Create audio",
        _ => capability.ToString(),
    };

    /// <summary>Every capability, in display order.</summary>
    public static IReadOnlyList<AiCapability> All { get; } = [.. Catalog.Keys];

    /// <summary>Options offered for a capability.</summary>
    public static IReadOnlyList<AiProviderOption> OptionsFor(AiCapability capability) => Catalog[capability];

    /// <summary>The default option — the first registered, which is the preferred one.</summary>
    public static AiProviderOption DefaultFor(AiCapability capability) => Catalog[capability][0];

    /// <summary>Looks up an option by capability and id, or null when unknown.</summary>
    public static AiProviderOption? Find(AiCapability capability, string? id) =>
        Catalog[capability].FirstOrDefault(o => o.Id == id);
}
```

- [ ] **Step 6: Create the selection state**

Create `src/PoRedoImage.Client/Models/AiSelectionState.cs`:

```csharp
namespace PoRedoImage.Client.Models;

/// <summary>
/// The session's per-capability provider choices.
/// </summary>
/// <remarks>
/// Registered scoped, which in Blazor WASM lasts the lifetime of the app instance: selections
/// survive navigation between Studio and the feature pages but reset on reload. That is deliberate —
/// nothing persists to storage, so a removed provider can never leave a stale selection behind.
/// It must be a service rather than page state because Studio is where the user chooses but the
/// feature pages are where requests are issued.
/// </remarks>
public sealed class AiSelectionState
{
    private readonly Dictionary<AiCapability, string> _selections = [];

    /// <summary>Raised after any selection changes so open components can re-render.</summary>
    public event Action? OnChange;

    /// <summary>The selected provider id, falling back to the catalog default.</summary>
    public string Get(AiCapability capability) =>
        _selections.TryGetValue(capability, out var id) ? id : AiServiceCatalog.DefaultFor(capability).Id;

    /// <summary>The selected option, falling back to the catalog default when the id is unknown.</summary>
    public AiProviderOption GetOption(AiCapability capability) =>
        AiServiceCatalog.Find(capability, Get(capability)) ?? AiServiceCatalog.DefaultFor(capability);

    /// <summary>Records a selection and notifies subscribers.</summary>
    public void Set(AiCapability capability, string providerId)
    {
        if (Get(capability) == providerId) return;

        _selections[capability] = providerId;
        OnChange?.Invoke();
    }
}
```

- [ ] **Step 7: Register the state in DI**

In `src/PoRedoImage.Client/Program.cs`, immediately after the existing
`builder.Services.AddScoped<PoRedoImage.Client.LocalAi.LocalAiService>();` line (~line 38), add:

```csharp
builder.Services.AddScoped<PoRedoImage.Client.Models.AiSelectionState>();
```

- [ ] **Step 8: Run the tests to verify they pass**

```powershell
dotnet test tests/PoRedoImage.Tests.Unit --filter "FullyQualifiedName~AiServiceCatalogTests"
dotnet test tests/PoRedoImage.Tests.Unit --filter "FullyQualifiedName~TestCountCeiling"
```

Expected: 4 passed, then the ceiling guardrail PASS. If the ceiling fails, move
`VisionServiceRouterTests` and `ImageGenerationRouterTests` to the Integration tier.

- [ ] **Step 9: Commit**

```bash
git add src/PoRedoImage.Client/Models/ src/PoRedoImage.Client/Program.cs \
        tests/PoRedoImage.Tests.Unit/Features/AiServiceCatalogTests.cs
git commit -m "feat(ai): add per-capability provider catalog and session selection state"
```

---

## Task 5: The picker component

**Files:**
- Create: `src/PoRedoImage.Client/Shared/AiServicePicker.razor`
- Create: `src/PoRedoImage.Client/Shared/AiServicePicker.razor.css`
- Delete: `src/PoRedoImage.Client/Shared/ModelCategoryPicker.razor`
- Delete: `src/PoRedoImage.Client/Shared/ModelCategoryPicker.razor.css`
- Modify: `src/PoRedoImage.Client/Pages/Studio.razor:34` and `:116`
- Test: `tests/PoRedoImage.Tests.E2E.UI/AiServicePickerUiTests.cs`

**Interfaces:**
- Consumes: `AiCapability`, `AiServiceCatalog`, `AiSelectionState` (Task 4), `LocalAiService` (existing)
- Produces: `<AiServicePicker />` — takes no parameters; reads and writes `AiSelectionState` via DI.

- [ ] **Step 1: Create the component**

Create `src/PoRedoImage.Client/Shared/AiServicePicker.razor`:

```razor
@namespace PoRedoImage.Client.Shared
@using PoRedoImage.Client.LocalAi
@using PoRedoImage.Client.Models
@inject LocalAiService LocalAi
@inject AiSelectionState Selection

@* One selector per capability. The categories are not interchangeable — Ollama and the browser
   models cannot generate images — so a single global picker could never be correct. *@
<fieldset class="ai-picker">
    <legend class="ai-picker__legend">AI services</legend>

    @foreach (var capability in AiServiceCatalog.All)
    {
        var options = AiServiceCatalog.OptionsFor(capability);
        var single = options.Count == 1;
        var selectId = $"ai-picker-{capability}";

        <div class="ai-picker__row">
            <label class="ai-picker__label" for="@selectId">@AiServiceCatalog.LabelFor(capability)</label>

            <select id="@selectId"
                    class="ai-picker__select"
                    disabled="@single"
                    title="@(single ? "Only provider configured for this capability" : null)"
                    value="@Selection.Get(capability)"
                    @onchange="@(e => OnSelected(capability, e.Value?.ToString()))">
                @foreach (var group in options.GroupBy(o => o.Category))
                {
                    <optgroup label="@group.Key">
                        @foreach (var option in group)
                        {
                            <option value="@option.Id">@option.DisplayName — @option.Hint</option>
                        }
                    </optgroup>
                }
            </select>
        </div>
    }

    @if (_showsBrowserModel)
    {
        <p class="ai-picker__device" role="status">
            @if (_browserUnsupported)
            {
                <span>
                    <i class="bi bi-exclamation-triangle me-1" aria-hidden="true"></i>
                    This browser has no WebGPU adapter, so models run on the CPU — slow, but supported.
                </span>
            }
            else
            {
                <span>
                    <i class="bi bi-gpu-card me-1" aria-hidden="true"></i>
                    WebGPU ready@(_capabilities?.HasShaderF16 == true ? " (shader-f16)" : "")@(_capabilities?.AdapterDescription is { Length: > 0 } d ? $" — {d}" : "")
                </span>
            }
        </p>
    }
</fieldset>

@code {
    private DeviceCapabilities? _capabilities;
    private bool _browserUnsupported;

    // Only meaningful once a browser-executed model is actually chosen — showing GPU status while
    // every capability is remote would be noise.
    private bool _showsBrowserModel =>
        AiServiceCatalog.All.Any(c => Selection.GetOption(c).ExecutesInBrowser);

    protected override async Task OnInitializedAsync()
    {
        _capabilities = await LocalAi.GetCapabilitiesAsync();

        // Not a blocker: DtypeChain prunes to wasm-capable variants and execution continues on the
        // CPU. This only drives the warning text.
        _browserUnsupported = !_capabilities.HasWebGpu;

        Selection.OnChange += OnSelectionChanged;
    }

    private void OnSelected(AiCapability capability, string? providerId)
    {
        if (!string.IsNullOrEmpty(providerId))
        {
            Selection.Set(capability, providerId);
        }
    }

    private void OnSelectionChanged() => InvokeAsync(StateHasChanged);

    // Deliberately NOT IAsyncDisposable: LocalAiService is a DI-owned scoped service shared with
    // every other component in this scope. Disposing it here would tear down its workers for them
    // too. The container owns its lifetime. Only the event subscription is released.
    public void Dispose() => Selection.OnChange -= OnSelectionChanged;
}
```

> Add `@implements IDisposable` directly beneath the `@inject` lines so the `Dispose` method above is
> actually invoked by the framework.

- [ ] **Step 2: Create the stylesheet**

Create `src/PoRedoImage.Client/Shared/AiServicePicker.razor.css`:

```css
.ai-picker {
    border: 1px solid var(--color-border);
    border-radius: var(--radius-lg);
    padding: var(--spacing-md);
    display: flex;
    flex-direction: column;
    gap: var(--spacing-sm);
    background: var(--color-surface);
}

.ai-picker__legend {
    font-size: 0.8125rem;
    font-weight: 600;
    letter-spacing: 0.02em;
    color: var(--color-body);
    padding: 0 var(--spacing-xs);
}

.ai-picker__row {
    display: grid;
    grid-template-columns: minmax(9rem, 12rem) 1fr;
    align-items: center;
    gap: var(--spacing-sm);
}

@media (max-width: 34rem) {
    .ai-picker__row {
        grid-template-columns: 1fr;
        gap: var(--spacing-2xs);
    }
}

.ai-picker__label {
    font-size: 0.8125rem;
    color: var(--color-muted);
}

.ai-picker__select {
    width: 100%;
    padding: 0.4rem 0.6rem;
    font-size: 0.8125rem;
    border: 1px solid var(--color-border);
    border-radius: var(--radius-md);
    background: var(--color-surface);
    color: var(--color-body);
}

.ai-picker__select:focus-visible {
    outline: 2px solid var(--color-primary);
    outline-offset: 1px;
}

.ai-picker__select:disabled {
    background: var(--color-surface-elevated);
    color: var(--color-muted);
    cursor: not-allowed;
}

.ai-picker__device {
    margin: 0;
    font-size: 0.75rem;
    color: var(--color-muted);
}
```

- [ ] **Step 3: Swap the component on Studio**

In `src/PoRedoImage.Client/Pages/Studio.razor`, replace line 34:

```razor
            <AiServicePicker />
```

and delete line 116 entirely:

```csharp
    private ModelCategoryPicker.ModelCategory _modelCategory = ModelCategoryPicker.ModelCategory.Remote;
```

- [ ] **Step 4: Delete the replaced component**

```bash
git rm src/PoRedoImage.Client/Shared/ModelCategoryPicker.razor \
       src/PoRedoImage.Client/Shared/ModelCategoryPicker.razor.css
```

- [ ] **Step 5: Build to verify nothing else referenced it**

```powershell
dotnet build PoRedoImage.slnx
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. A CS0246 for `ModelCategoryPicker` means a
reference was missed — fix it before continuing.

- [ ] **Step 6: Write the E2E UI test**

Create `tests/PoRedoImage.Tests.E2E.UI/AiServicePickerUiTests.cs`. Read
`tests/PoRedoImage.Tests.E2E.UI/LoginUiTests.cs` first for the sign-in helper and base-URL handling,
then write (class name contains "Ui" so the ceiling test partitions it correctly):

```csharp
using Microsoft.Playwright;

namespace PoRedoImage.Tests.E2E.UI;

/// <summary>
/// The four single-provider capabilities render as disabled selects. Asserting on that is what stops
/// a future refactor from silently making them look choosable when nothing would change.
/// </summary>
public sealed class AiServicePickerUiTests : IAsyncLifetime
{
    private IPlaywright _playwright = default!;
    private IBrowser _browser = default!;

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
    }

    [LiveServerFact]
    public async Task Studio_renders_a_selector_per_capability_with_single_provider_ones_disabled()
    {
        var page = await _browser.NewPageAsync();
        await SignInAsGuestAsync(page);
        await page.GotoAsync(BaseUrl, new() { WaitUntil = WaitUntilState.NetworkIdle });

        var selects = page.Locator(".ai-picker__select");
        await Assertions.Expect(selects).ToHaveCountAsync(6);

        await Assertions.Expect(page.Locator("#ai-picker-AnalyzeImage")).ToBeEnabledAsync();
        await Assertions.Expect(page.Locator("#ai-picker-GenerateImage")).ToBeEnabledAsync();
        await Assertions.Expect(page.Locator("#ai-picker-StyleDirector")).ToBeDisabledAsync();
        await Assertions.Expect(page.Locator("#ai-picker-SceneDetail")).ToBeDisabledAsync();
        await Assertions.Expect(page.Locator("#ai-picker-CreateAudio")).ToBeDisabledAsync();
    }

    public async Task DisposeAsync()
    {
        await _browser.CloseAsync();
        _playwright.Dispose();
    }
}
```

> Replace `BaseUrl` and `SignInAsGuestAsync` with the actual helpers used in `LoginUiTests.cs`
> (`E2E_BASE_URL` defaults to `http://localhost:4000`; sign-in goes through `/auth/login/fake`).

- [ ] **Step 7: Run the UI test against a live instance**

```powershell
# terminal 1 — leave running
dotnet run --project src/PoRedoImage.Web
# terminal 2
dotnet test tests/PoRedoImage.Tests.E2E.UI --filter "FullyQualifiedName~AiServicePickerUiTests"
```

Expected: PASS. `[LiveServerFact]` self-skips if no instance is reachable — a *skipped* result means
the server was not running, not that the test passed.

- [ ] **Step 8: Commit**

```bash
git add src/PoRedoImage.Client/Shared/AiServicePicker.razor \
        src/PoRedoImage.Client/Shared/AiServicePicker.razor.css \
        src/PoRedoImage.Client/Pages/Studio.razor \
        tests/PoRedoImage.Tests.E2E.UI/AiServicePickerUiTests.cs
git commit -m "feat(ai): replace ModelCategoryPicker with per-capability AiServicePicker"
```

---

## Task 6: Send remote selections with each request

Makes the two remote-provider dropdowns actually route work. Browser-local execution is Task 7.

**Files:**
- Modify: `src/PoRedoImage.Client/Pages/FeaturePageBase.cs`
- Modify: `src/PoRedoImage.Client/Pages/ImageRegeneration.razor:96-112`
- Modify: `src/PoRedoImage.Client/Pages/MemeGeneration.razor:217-233`

**Interfaces:**
- Consumes: `AiSelectionState`, `AiCapability` (Task 4); `ImageAnalysisRequest.ImageGenModelId` (Task 3)
- Produces: `FeaturePageBase.BuildAnalysisRequest(string imageData, string contentType, string fileName, int descriptionLength, ProcessingMode mode) → ImageAnalysisRequest`. Task 7 extends this method.

- [ ] **Step 1: Add the injected state and request builder**

In `src/PoRedoImage.Client/Pages/FeaturePageBase.cs`, add to the `[Inject]` block:

```csharp
    [Inject] protected AiSelectionState AiSelection { get; set; } = default!;
```

Add the `using` at the top:

```csharp
using PoRedoImage.Client.Models;
```

Then add this method to the class:

```csharp
    /// <summary>
    /// Builds an analysis request carrying the session's provider selections.
    /// </summary>
    /// <remarks>
    /// Centralised here rather than duplicated in ImageRegeneration and MemeGeneration: both pages
    /// build the same request and would otherwise drift the moment a new field is added.
    /// </remarks>
    protected ImageAnalysisRequest BuildAnalysisRequest(
        string imageData,
        string contentType,
        string fileName,
        int descriptionLength,
        ProcessingMode mode) => new()
        {
            ImageData = imageData,
            ContentType = contentType,
            FileName = fileName,
            DescriptionLength = descriptionLength,
            Mode = mode,
            ModelId = AiSelection.Get(AiCapability.AnalyzeImage),
            ImageGenModelId = AiSelection.Get(AiCapability.GenerateImage),
        };
```

- [ ] **Step 2: Use the builder in ImageRegeneration**

In `src/PoRedoImage.Client/Pages/ImageRegeneration.razor`, replace the `new ImageAnalysisRequest { ... }`
object initializer at line 96 with a call to the builder, passing the same values that initializer
used:

```csharp
            var request = BuildAnalysisRequest(
                imageData: base64,
                contentType: selectedFile!.ContentType,
                fileName: selectedFile.Name,
                descriptionLength: descriptionLength,
                mode: ProcessingMode.ImageRegeneration);
```

> Match the local variable names already in that method — read the surrounding lines and substitute
> the existing image-data, content-type, file-name, and description-length expressions verbatim.

- [ ] **Step 3: Use the builder in MemeGeneration**

Apply the same replacement at `src/PoRedoImage.Client/Pages/MemeGeneration.razor:217`, with
`mode: ProcessingMode.MemeGeneration`.

- [ ] **Step 4: Build**

```powershell
dotnet build PoRedoImage.slnx
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 5: Verify the selection reaches the server**

Start the app, sign in as GUEST, open Studio, set **Generate image** to *FLUX.1-schnell*, upload an
image, and run a regeneration. Confirm in the server log that the HuggingFace provider handled it:

```powershell
dotnet run --project src/PoRedoImage.Web
```

Expected: the request log shows the HuggingFace image-generation path, not Gemini.

- [ ] **Step 6: Commit**

```bash
git add src/PoRedoImage.Client/Pages/FeaturePageBase.cs \
        src/PoRedoImage.Client/Pages/ImageRegeneration.razor \
        src/PoRedoImage.Client/Pages/MemeGeneration.razor
git commit -m "feat(ai): send per-capability provider selections with analysis requests"
```

---

## Task 7: Browser-local execution path

When Analyze image is set to a browser model, run it client-side and post the result instead of
asking the server to do vision.

**Files:**
- Modify: `src/PoRedoImage.Client/Pages/FeaturePageBase.cs`

**Interfaces:**
- Consumes: `BuildAnalysisRequest` (Task 6); `LocalAiService.DescribeImageAsync`, `LocalInferenceOutcome`, `LocalInferenceException`, `LocalAiErrorClassifier` (existing); `ImageAnalysisRequest.PrecomputedDescription` / `.PrecomputedTags` (Task 3)
- Produces: nothing consumed by later tasks — this is the final task.

- [ ] **Step 1: Inject the local runtime**

In `src/PoRedoImage.Client/Pages/FeaturePageBase.cs`, add to the `[Inject]` block:

```csharp
    [Inject] protected LocalAiService LocalAi { get; set; } = default!;
```

and the `using`:

```csharp
using PoRedoImage.Client.LocalAi;
```

- [ ] **Step 2: Make the request builder async and run local inference**

Replace the `BuildAnalysisRequest` method added in Task 6 with:

```csharp
    /// <summary>
    /// Builds an analysis request carrying the session's provider selections, running vision
    /// in-browser first when the user picked a browser-executed model.
    /// </summary>
    /// <remarks>
    /// A local failure is surfaced, never silently retried against the remote provider: quietly
    /// switching from a free on-device model to a metered API is a billing surprise, so the user
    /// decides.
    /// </remarks>
    protected async Task<ImageAnalysisRequest> BuildAnalysisRequestAsync(
        byte[] imageBytes,
        string imageData,
        string contentType,
        string fileName,
        int descriptionLength,
        ProcessingMode mode,
        CancellationToken ct = default)
    {
        var request = new ImageAnalysisRequest
        {
            ImageData = imageData,
            ContentType = contentType,
            FileName = fileName,
            DescriptionLength = descriptionLength,
            Mode = mode,
            ModelId = AiSelection.Get(AiCapability.AnalyzeImage),
            ImageGenModelId = AiSelection.Get(AiCapability.GenerateImage),
        };

        if (!AiSelection.GetOption(AiCapability.AnalyzeImage).ExecutesInBrowser)
        {
            return request;
        }

        var outcome = await LocalAi.DescribeImageAsync(
            imageBytes,
            prompt: "Describe this image and list its subjects.",
            progress: new Progress<LocalInferenceStatus>(OnLocalProgress),
            ct: ct);

        request.PrecomputedDescription = outcome.Text;
        request.PrecomputedTags = ExtractTags(outcome.Text);
        return request;
    }

    /// <summary>
    /// Derives coarse tags from a local model's free-text description. Browser vision models emit
    /// prose, not a tag list, and the server's meme branch needs tags to caption from.
    /// </summary>
    private static IReadOnlyList<string> ExtractTags(string description) =>
        [.. description
            .Split([' ', ',', '.', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => w.Length > 3)
            .Select(w => w.ToLowerInvariant())
            .Distinct()
            .Take(10)];

    /// <summary>Surfaces local-inference progress through the existing progress UI.</summary>
    private void OnLocalProgress(LocalInferenceStatus status)
    {
        progressMessage = status.Stage switch
        {
            LocalStage.Probing => "Checking your device…",
            LocalStage.Downloading => $"Downloading model… {status.LoadPercent ?? 0}%",
            LocalStage.Loading => "Loading model…",
            LocalStage.Running => "Analyzing on your device…",
            _ => progressMessage,
        };

        InvokeAsync(StateHasChanged);
    }
```

> `progressMessage` is the existing progress field on `FeaturePageBase`. Read the class first and use
> its actual field name; if the base exposes a progress-setting method instead, call that.

- [ ] **Step 3: Handle local failure at the call sites**

In both `ImageRegeneration.razor` and `MemeGeneration.razor`, wrap the builder call so a local
failure is reported rather than crashing the page:

```csharp
            ImageAnalysisRequest request;
            try
            {
                request = await BuildAnalysisRequestAsync(
                    imageBytes: fileBytes,
                    imageData: base64,
                    contentType: selectedFile!.ContentType,
                    fileName: selectedFile.Name,
                    descriptionLength: descriptionLength,
                    mode: ProcessingMode.ImageRegeneration,
                    ct: cts.Token);
            }
            catch (LocalInferenceException ex)
            {
                errorMessage = LocalAiErrorClassifier.Describe(ex.Failure);
                isProcessing = false;
                return;
            }
```

> Use `ProcessingMode.MemeGeneration` in `MemeGeneration.razor`. Read
> `src/PoRedoImage.Client/LocalAi/LocalAiErrorClassifier.cs` and call its actual public method — if it
> exposes a different name than `Describe`, use that and keep the message user-facing.

- [ ] **Step 4: Build**

```powershell
dotnet build PoRedoImage.slnx
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 5: Verify the local path end to end**

Start the app, sign in, set **Analyze image** to *Florence-2 base*, upload an image, and run a
regeneration.

Expected: the progress UI shows the download and analysis stages; the browser network panel shows
**no** call to Azure Computer Vision; the response's `ImageAnalysisTimeMs` is `0`.

- [ ] **Step 6: Run every tier**

```powershell
dotnet test tests/PoRedoImage.Tests.Unit
dotnet test tests/PoRedoImage.Tests.Integration
dotnet test tests/PoRedoImage.Tests.E2E.ApiSmoke
dotnet test tests/PoRedoImage.Tests.E2E.UI
```

Expected: all PASS, including all four ceiling guardrails.

- [ ] **Step 7: Update the architecture doc**

In `AGENT.MD`, update the **AI Models (Rule 14)** section to describe per-capability selection
replacing the three-way category picker, and note the `AiProviderIds` namespacing scheme.

- [ ] **Step 8: Commit**

```bash
git add src/PoRedoImage.Client/Pages/FeaturePageBase.cs \
        src/PoRedoImage.Client/Pages/ImageRegeneration.razor \
        src/PoRedoImage.Client/Pages/MemeGeneration.razor \
        AGENT.MD
git commit -m "feat(ai): run vision in-browser when a browser model is selected"
```

---

## Self-Review

**Spec coverage**

| Spec requirement | Task |
|---|---|
| Six selectors, one per capability | 5 |
| `<optgroup>` grouping by category | 5 |
| Single-provider capabilities disabled with explanatory title | 5 |
| Browser models included where they can serve | 4 (catalog), 7 (execution) |
| Session-only, no localStorage | 4 |
| Replaces `ModelCategoryPicker` on Studio | 5 |
| `AiServiceCatalog` derives browser entries from `LocalModelRegistry` | 4 |
| `AiSelectionState` scoped | 4 |
| Id namespacing + router tightening | 1 |
| `ImageGenerationRouter` with config-flag fallback | 2 |
| Mock mode single-service router | 2 |
| Three new DTO fields | 3 |
| Precomputed-vision branch, confidence 1.0 | 3 |
| No silent fallback on local failure | 7 |
| Unit / Integration / E2E UI coverage | 1, 2, 3, 4, 5 |

No gaps.

**Known deviation from the spec:** the spec lists Qwen2.5 as an Enhance-description option, and Task 4
includes it in the catalog. Task 7 implements browser execution for **Analyze image only**.
`LocalAiService.CompleteTextAsync` exists and the wiring is symmetric, but the enhancement step runs
server-side *after* vision, so routing it to the browser requires a second round trip. Selecting
Qwen2.5 for Enhance description therefore falls through to Azure OpenAI until that is built. Flagged
here rather than hidden — it needs either a follow-up task or removal from the catalog.

**Type consistency:** `AiProviderOption.ExecutesInBrowser` is used identically in Tasks 4, 5, 7.
`AiSelectionState.Get`/`GetOption`/`Set` match across Tasks 4-7. `IImageGenerationRouter.Resolve`
matches between Tasks 2 and 3. `BuildAnalysisRequest` (Task 6) is deliberately superseded by
`BuildAnalysisRequestAsync` (Task 7) — Task 7 Step 2 says "replace", and Task 7 Step 3 updates both
call sites.
