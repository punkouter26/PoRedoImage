# Generate editorial HTML diagrams from .mmd files
# Following cathrynlavery/diagram-design aesthetic

$docsDir = $PSScriptRoot

# Diagram metadata: filename → [title, eyebrow/category, subtitle, card1, card2, card3]
$diagrams = @{
  "Architecture_MASTER" = @{
    Title    = "Master Architecture"
    Category = "ARCHITECTURE · MASTER"
    Subtitle = "Complete system architecture: edge delivery, compute (Blazor + Minimal API), three AI services, Azure Table Storage, OpenTelemetry observability, and GitHub Actions CI/CD pipeline."
    Cards    = @(
      @{ Eyebrow="COMPUTE"; Dot="coral"; Heading="Blazor + Minimal API"; Items=@("Blazor SSR + Interactive Server (.NET 10)","Auth: Cookie + Microsoft Entra OIDC","Rate limiting: 10 req/min per user","/health · /diag · Scalar API docs") },
      @{ Eyebrow="AI SERVICES"; Dot="link"; Heading="Three-Layer AI Pipeline"; Items=@("Azure Computer Vision — image tagging","Azure OpenAI GPT-4.1-nano — description/caption","Google Gemini — image regeneration & bulk") },
      @{ Eyebrow="OPERATIONS"; Dot="ink"; Heading="Observability Stack"; Items=@("OpenTelemetry auto-instrumentation","Serilog structured logs → App Insights","GitHub Actions: CI + E2E + OIDC deploy") }
    )
  }
  "Architecture_MASTER_SIMPLE" = @{
    Title    = "Architecture Overview"
    Category = "ARCHITECTURE · OVERVIEW"
    Subtitle = "Simplified view of the five key system zones: user, Blazor app, AI services, data layer, and observability."
    Cards    = @(
      @{ Eyebrow="FRONTEND"; Dot="coral"; Heading="Blazor Web App"; Items=@("Azure App Service hosting",".NET 10 SSR + WASM unified","HTTPS from CDN or direct") },
      @{ Eyebrow="AI LAYER"; Dot="link"; Heading="AI Services"; Items=@("Computer Vision · OpenAI · Gemini","Single orchestrator entry point","Parallel bulk generation") },
      @{ Eyebrow="DATA"; Dot="ink"; Heading="Persistence & Ops"; Items=@("Azure Table Storage","Azure Key Vault secrets","Application Insights telemetry") }
    )
  }
  "DataLifecycle_MASTER" = @{
    Title    = "Data Lifecycle"
    Category = "DATA FLOW · MASTER"
    Subtitle = "End-to-end image data lifecycle: browser ingestion → HTTPS transfer with magic-byte validation → AI processing pipeline → optional persistence in Azure Table Storage."
    Cards    = @(
      @{ Eyebrow="INGESTION"; Dot="coral"; Heading="Client-Side Encoding"; Items=@("File picker or clipboard paste","FileReader.readAsDataURL → base64","Single POST body — no multipart") },
      @{ Eyebrow="PROCESSING"; Dot="link"; Heading="AI Pipeline Branches"; Items=@("ImageRegeneration: CV → OpenAI → Gemini","MemeGeneration: CV → OpenAI caption → SkiaSharp","Magic byte guard: JPEG FF D8 FF / PNG 89 50 4E 47") },
      @{ Eyebrow="PERSISTENCE"; Dot="ink"; Heading="Optional Table Storage"; Items=@("User-initiated save only","PartitionKey=UserId, RowKey=GUID","Kind enum: Original / Regeneration / Meme / BulkVariation") }
    )
  }
  "DataLifecycle_MASTER_SIMPLE" = @{
    Title    = "Data Lifecycle Overview"
    Category = "DATA FLOW · OVERVIEW"
    Subtitle = "Simplified pipeline: upload base64 → validate → Computer Vision → OpenAI → Gemini → render → optional save to Table Storage."
    Cards    = @(
      @{ Eyebrow="VALIDATE"; Dot="coral"; Heading="Magic Byte Guard"; Items=@("JPEG: FF D8 FF","PNG: 89 50 4E 47","HTTP 400 on invalid bytes") },
      @{ Eyebrow="PROCESS"; Dot="link"; Heading="AI Chain"; Items=@("Computer Vision: tags + confidence","OpenAI: enhance description or caption","Gemini: regenerate image bytes") },
      @{ Eyebrow="OUTPUT"; Dot="ink"; Heading="Render + Save"; Items=@("Blazor renders base64 img src","User opts in to gallery save","Table Storage upsert") }
    )
  }
  "DataModel" = @{
    Title    = "Data Model"
    Category = "DATA MODEL · ER DIAGRAM"
    Subtitle = "Entity-relationship model for PoRedoImage: USER owns many USER_IMAGEs and has one BULK_PROMPT. Supporting enums define image kind and processing mode."
    Cards    = @(
      @{ Eyebrow="USER"; Dot="coral"; Heading="Identity Entity"; Items=@("UserId = ClaimTypes.NameIdentifier","AuthScheme: cookie | oidc","DisplayName + Email from claims") },
      @{ Eyebrow="USER_IMAGE"; Dot="link"; Heading="Image Record"; Items=@("PartitionKey = UserId","RowKey = Guid (N-format)","Kind: Original / Regeneration / Meme / BulkVariation") },
      @{ Eyebrow="BULK_PROMPT"; Dot="ink"; Heading="Prompt Storage"; Items=@("One record per user","PromptText = JSON array of 10 strings","PartitionKey = 'prompts', RowKey = UserId") }
    )
  }
  "DataModel_SIMPLE" = @{
    Title    = "Data Model Overview"
    Category = "DATA MODEL · OVERVIEW"
    Subtitle = "Three core entities: USER owns USER_IMAGEs and has one BULK_PROMPT. Minimal ER view."
    Cards    = @(
      @{ Eyebrow="ENTITIES"; Dot="coral"; Heading="Three Tables"; Items=@("USER — identity","USER_IMAGE — saved results","BULK_PROMPT — user's 10 prompts") },
      @{ Eyebrow="RELATIONS"; Dot="link"; Heading="Ownership Rules"; Items=@("User owns many images","User has at most one prompt set","All queries scoped to UserId") },
      @{ Eyebrow="STORAGE"; Dot="ink"; Heading="Azure Table Storage"; Items=@("Account: stporedoimage26","No SQL — key-value entities","PartitionKey always = UserId") }
    )
  }
  "ExceptionUserFlows" = @{
    Title    = "Exception & Error Flows"
    Category = "ERROR HANDLING · MASTER"
    Subtitle = "All exception paths: authentication redirects, input validation (magic bytes, base64, length), rate limiting HTTP 429, AI content policy HTTP 422, and service failures."
    Cards    = @(
      @{ Eyebrow="VALIDATION"; Dot="coral"; Heading="Input Guard Errors"; Items=@("HTTP 400: invalid base64 / magic bytes","HTTP 400: DescriptionLength 200–500","HTTP 400: exactly 10 prompts required","Open-redirect prevention on returnUrl") },
      @{ Eyebrow="RATE & POLICY"; Dot="link"; Heading="Throttle + AI Policy"; Items=@("HTTP 429: 10 req/min per user","Retry-After header returned","HTTP 422: content_policy_violation","Try a different image prompt") },
      @{ Eyebrow="SERVICE FAULTS"; Dot="ink"; Heading="AI & Storage Failures"; Items=@("CV timeout → HTTP 500 + LogError","Gemini not configured → HTTP 400","OpenAI throttle → propagate retry-after","Table Storage fail → /health 503 Degraded") }
    )
  }
  "ExceptionUserFlows_SIMPLE" = @{
    Title    = "Error Flows Overview"
    Category = "ERROR HANDLING · OVERVIEW"
    Subtitle = "Six error categories at a glance: auth redirect, validation 400, rate limit 429, content policy 422, AI failure 500, storage degraded."
    Cards    = @(
      @{ Eyebrow="CLIENT ERRORS"; Dot="coral"; Heading="4xx Responses"; Items=@("400: bad input / invalid image","422: content policy blocked","429: rate limit exceeded") },
      @{ Eyebrow="SERVER ERRORS"; Dot="link"; Heading="5xx Responses"; Items=@("500: AI service unavailable","503: /health degraded","Serilog logs with correlation ID") },
      @{ Eyebrow="AUTH ERRORS"; Dot="ink"; Heading="Auth Redirects"; Items=@("401 → /login redirect","OIDC failure displays error description","Dev login requires email param") }
    )
  }
  "InterfaceHierarchy_MASTER" = @{
    Title    = "Interface Hierarchy & Component Map"
    Category = "COMPONENT HIERARCHY · MASTER"
    Subtitle = "Full UI component tree from App.razor root through layouts, SSR page hosts, WASM interactive components, and shared UI primitives. State management via DI and Radzen."
    Cards    = @(
      @{ Eyebrow="SSR LAYER"; Dot="coral"; Heading="Server-Side Pages"; Items=@("Studio.razor — SSR host for client Studio","BulkGenerate.razor — SSR host","Login.razor — Dev + Microsoft OIDC","Diag.razor — masked config JSON dump") },
      @{ Eyebrow="WASM LAYER"; Dot="link"; Heading="Interactive Components"; Items=@("Studio.razor — upload + mode + results","BulkGenerate.razor — 10 prompts + grid","UserImages.razor — gallery + delete") },
      @{ Eyebrow="PRIMITIVES"; Dot="ink"; Heading="Shared UI Components"; Items=@("ImageUploadPanel — file/drag/paste/preview","ImageRegenerationResultsPanel — before/after","MemeResultsPanel — overlay + download","ProcessingProgressBar — step + elapsed time") }
    )
  }
  "InterfaceHierarchy_MASTER_SIMPLE" = @{
    Title    = "Component Hierarchy Overview"
    Category = "COMPONENT HIERARCHY · OVERVIEW"
    Subtitle = "App.razor → Layout → four page routes → shared UI components (UploadPanel, ResultPanels, BulkGrid, ProgressBar)."
    Cards    = @(
      @{ Eyebrow="ROUTING"; Dot="coral"; Heading="App.razor Router"; Items=@("Route: / Studio","Route: /bulk-generate","Route: /user-images","Route: /diag") },
      @{ Eyebrow="PAGES"; Dot="link"; Heading="Four Main Pages"; Items=@("Studio — primary AI workflow","BulkGenerate — 10× parallel","UserImages — gallery","Diag — diagnostics") },
      @{ Eyebrow="SHARED"; Dot="ink"; Heading="Reused Components"; Items=@("ImageUploadPanel — used in 2 pages","ProcessingProgressBar — used in 2 pages","ResultPanels — Studio only","BulkResultsGrid — BulkGenerate only") }
    )
  }
  "OnboardingJourney" = @{
    Title    = "Onboarding Journey"
    Category = "USER JOURNEY · MASTER"
    Subtitle = "First-run flow from unauthenticated arrival through environment-specific login (Dev cookie or Prod OIDC) to the Studio 'Aha!' moment and gallery save."
    Cards    = @(
      @{ Eyebrow="AUTHENTICATION"; Dot="coral"; Heading="Two Login Paths"; Items=@("Dev: /dev-login?email= → instant cookie","Prod: Microsoft Entra OIDC → consent → callback","Claims: NameIdentifier + Email + Name") },
      @{ Eyebrow="FIRST USE"; Dot="link"; Heading="Studio Aha Moment"; Items=@("Upload any JPEG/PNG photo","Choose: ImageRegeneration or MemeGeneration","AI result displayed in seconds") },
      @{ Eyebrow="RETENTION"; Dot="ink"; Heading="Gallery Save"; Items=@("One-click save to personal gallery","POST /api/user-images/save-result","View all saved images at /user-images") }
    )
  }
  "OnboardingJourney_SIMPLE" = @{
    Title    = "Onboarding Overview"
    Category = "USER JOURNEY · OVERVIEW"
    Subtitle = "Seven-step simplified onboarding: arrive → login → studio → upload → AI result → save → gallery."
    Cards    = @(
      @{ Eyebrow="STEP 1–2"; Dot="coral"; Heading="Login"; Items=@("Dev: instant cookie login","Prod: Microsoft Entra OIDC","No account creation required") },
      @{ Eyebrow="STEP 3–5"; Dot="link"; Heading="First AI Result"; Items=@("Navigate to Studio","Upload any photo","See AI regeneration or meme") },
      @{ Eyebrow="STEP 6–7"; Dot="ink"; Heading="Save & Browse"; Items=@("Save result to personal gallery","Browse My Images at /user-images","Delete unwanted results") }
    )
  }
  "PrimaryValueFlow" = @{
    Title    = "Primary Value Flow — Bulk Generate"
    Category = "USER FLOW · MASTER"
    Subtitle = "Bulk Generate workflow: upload photo → optionally edit & save 10 art-style prompts → GPT-4.1-nano person description → 10 parallel Gemini Imagen3 calls → live streaming results grid."
    Cards    = @(
      @{ Eyebrow="SETUP"; Dot="coral"; Heading="Prompt Management"; Items=@("10 pre-loaded art-style prompts","Edit text (max 2000 chars each)","Persist to Table Storage per user","Load saved prompts on return") },
      @{ Eyebrow="PIPELINE"; Dot="link"; Heading="Parallel AI Execution"; Items=@("POST /describe — GPT-4.1-nano vision","10× POST /variation — Gemini Imagen3","No slot blocks another","Results stream live as each completes") },
      @{ Eyebrow="OUTPUT"; Dot="ink"; Heading="BulkResultsGrid"; Items=@("10-image grid rendered progressively","Progress bar shows slot completion","Download individual or save to gallery","ProgressBar step indicator + elapsed time") }
    )
  }
  "PrimaryValueFlow_SIMPLE" = @{
    Title    = "Bulk Generate Overview"
    Category = "USER FLOW · OVERVIEW"
    Subtitle = "Simplified bulk flow: upload → GPT describe → 10× parallel Gemini → live stream → save."
    Cards    = @(
      @{ Eyebrow="INPUT"; Dot="coral"; Heading="Photo + Prompts"; Items=@("Upload reference photo","10 art-style text prompts","Edit or use defaults") },
      @{ Eyebrow="GENERATE"; Dot="link"; Heading="Parallel Gemini"; Items=@("GPT-4.1-nano describes person","10 parallel Imagen3 calls","Results appear as they complete") },
      @{ Eyebrow="OUTPUT"; Dot="ink"; Heading="Live Grid"; Items=@("10-image result grid","Save any image to gallery","Download locally") }
    )
  }
  "ReleasePipeline_MASTER" = @{
    Title    = "Release Pipeline"
    Category = "CI/CD · MASTER"
    Subtitle = "Three GitHub Actions workflows: ci.yml on PRs (build + unit + integration + coverage), e2e.yml (Playwright headless), and azure-deploy.yml (OIDC → App Service zip deploy + smoke check)."
    Cards    = @(
      @{ Eyebrow="CI GATE"; Dot="coral"; Heading="ci.yml on Pull Requests"; Items=@("dotnet build — WarningsAsErrors","Unit tests: pure logic (xUnit)","Integration: Testcontainers + Azurite","Coverage gate: ≥80% opencover (warn-only)") },
      @{ Eyebrow="E2E GATE"; Dot="link"; Heading="e2e.yml — Playwright"; Items=@("npm ci + playwright install","Start ASP.NET in Development mode","Headless Chromium + mobile viewports","Upload report + screenshots on failure") },
      @{ Eyebrow="DEPLOY"; Dot="ink"; Heading="azure-deploy.yml on master"; Items=@("dotnet publish Release","OIDC federated identity — no long-lived secrets","az webapp deploy zip → OneDeploy API","Smoke: curl /health → 200") }
    )
  }
  "ReleasePipeline_MASTER_SIMPLE" = @{
    Title    = "Release Pipeline Overview"
    Category = "CI/CD · OVERVIEW"
    Subtitle = "Four-stage pipeline: PR triggers CI (build + test), pass → merge, master triggers OIDC deploy, smoke test confirms live."
    Cards    = @(
      @{ Eyebrow="TRIGGER"; Dot="coral"; Heading="Pull Request → CI"; Items=@("ci.yml triggers on PR","Build + test + coverage","Must pass before merge") },
      @{ Eyebrow="DEPLOY"; Dot="link"; Heading="master → Azure"; Items=@("azure-deploy.yml on push","OIDC — no stored secrets","Zip deploy to App Service") },
      @{ Eyebrow="VERIFY"; Dot="ink"; Heading="Smoke Check"; Items=@("curl /health endpoint","200 = deployment successful","Fail = rollback signal") }
    )
  }
  "ServiceMap_MASTER" = @{
    Title    = "Service Map"
    Category = "ARCHITECTURE · SERVICE MAP"
    Subtitle = "Full project dependency map: PoRedoImage.Web hosts VSA feature slices, delegates to Application layer, which depends on Domain interfaces implemented by Infrastructure, all sharing DTOs via PoRedoImage.Shared."
    Cards    = @(
      @{ Eyebrow="WEB PROJECT"; Dot="coral"; Heading="PoRedoImage.Web"; Items=@("Program.cs — composition root","5 VSA feature slices (endpoints)","SSR + WASM components","Key Vault · Serilog · OTel · Auth · Radzen") },
      @{ Eyebrow="CORE"; Dot="link"; Heading="Application + Domain"; Items=@("IImageAnalysisOrchestrator — single entry","Domain: Entities + 6 Interfaces","Infrastructure: 4 Services + 2 Repositories","Dependency inversion: Domain → Infra") },
      @{ Eyebrow="SHARED"; Dot="ink"; Heading="PoRedoImage.Shared"; Items=@("DTOs for all API contracts","ProcessingMode + UserImageKind enums","BulkGenerate + UserImage DTOs","Used by Web, App, Infra, Client") }
    )
  }
  "ServiceMap_MASTER_SIMPLE" = @{
    Title    = "Service Map Overview"
    Category = "ARCHITECTURE · OVERVIEW"
    Subtitle = "Simplified 6-node dependency graph: Web → App → Domain → Infrastructure → Shared ← Client."
    Cards    = @(
      @{ Eyebrow="HOST"; Dot="coral"; Heading="Web + Client"; Items=@("PoRedoImage.Web (SSR host)","PoRedoImage.Client (WASM)","Both reference Shared DTOs") },
      @{ Eyebrow="CORE"; Dot="link"; Heading="Application + Domain"; Items=@("Single orchestrator interface","Domain entities + service interfaces","Clean architecture boundaries") },
      @{ Eyebrow="INFRA"; Dot="ink"; Heading="Infrastructure + Shared"; Items=@("Implements all Domain interfaces","Azure CV, OpenAI, Gemini, SkiaSharp","Shared DTO package for API contracts") }
    )
  }
  "StateDynamics_MASTER" = @{
    Title    = "State Dynamics"
    Category = "STATE MACHINE · MASTER"
    Subtitle = "Three concurrent state machines: UserImage lifecycle (upload → analyze → enhance → regenerate → save), BulkVariation lifecycle, and BulkPrompt lifecycle (default → edit → persist)."
    Cards    = @(
      @{ Eyebrow="IMAGE LIFECYCLE"; Dot="coral"; Heading="Upload → Save"; Items=@("Rejected: invalid magic bytes → terminal","ContentBlocked: HTTP 422 → terminal","Saveable: Regenerated or MemeGenerated","Saved: TableStorage upsert → gallery entry") },
      @{ Eyebrow="BULK LIFECYCLE"; Dot="link"; Heading="Variations"; Items=@("PromptLoaded → Described (GPT vision)","10× parallel GeneratingVariations","VariationComplete → optional BulkSaved","VariationFailed: Gemini quota/config") },
      @{ Eyebrow="PROMPT LIFECYCLE"; Dot="ink"; Heading="Prompt Persistence"; Items=@("Default 10 prompts on app load","PendingSave when user edits","POST /prompts → Persisted in Table Storage","User can reset back to defaults") }
    )
  }
  "StateDynamics_MASTER_SIMPLE" = @{
    Title    = "State Dynamics Overview"
    Category = "STATE MACHINE · OVERVIEW"
    Subtitle = "Simplified image lifecycle: Uploaded → Analyzing → Regenerated (success) or Failed (error) → Saved → terminal."
    Cards    = @(
      @{ Eyebrow="HAPPY PATH"; Dot="coral"; Heading="Success Flow"; Items=@("Uploaded → Analyzing","Analyzing → Regenerated","Regenerated → Saved → done") },
      @{ Eyebrow="ERROR PATH"; Dot="link"; Heading="Failure States"; Items=@("Magic byte fail → Rejected","CV/AI failure → Failed","Content policy → ContentBlocked") },
      @{ Eyebrow="SAVE"; Dot="ink"; Heading="Persistence"; Items=@("User-initiated only","POST /api/user-images/save-result","Creates gallery entry") }
    )
  }
  "SystemFlow_MASTER" = @{
    Title    = "System Flow"
    Category = "SEQUENCE · MASTER"
    Subtitle = "Full sequence diagram: Key Vault startup, authentication, image analysis (CV → OpenAI → Gemini), gallery save, and 10-slot parallel bulk generation."
    Cards    = @(
      @{ Eyebrow="STARTUP"; Dot="coral"; Heading="Key Vault + Auth"; Items=@("AddAzureKeyVault on startup (30-min rotation)","DefaultAzureCredential — Managed Identity","OIDC: challenge → consent → callback → cookie","Dev: /dev-login?email= → instant cookie") },
      @{ Eyebrow="ANALYSIS"; Dot="link"; Heading="Image Processing"; Items=@("Magic byte validation first","CV → tags + confidence","OpenAI enhance description","Gemini regenerate image bytes") },
      @{ Eyebrow="BULK"; Dot="ink"; Heading="10× Parallel Slots"; Items=@("POST /describe — person context","10 concurrent /variation calls","Results delivered as each slot resolves","No slot blocks another") }
    )
  }
  "SystemFlow_MASTER_SIMPLE" = @{
    Title    = "System Flow Overview"
    Category = "SEQUENCE · OVERVIEW"
    Subtitle = "Simplified 8-message sequence: login → POST /analyze → AI pipeline → display result → save to gallery."
    Cards    = @(
      @{ Eyebrow="AUTH"; Dot="coral"; Heading="Login"; Items=@("Any environment login","Cookie issued","Redirect to Studio") },
      @{ Eyebrow="PROCESS"; Dot="link"; Heading="AI Pipeline"; Items=@("POST /analyze","Vision + OpenAI + Gemini","ImageAnalysisResponse") },
      @{ Eyebrow="SAVE"; Dot="ink"; Heading="Gallery"; Items=@("User clicks Save","POST /save-result","Table Storage persist") }
    )
  }
  "SystemInteractionFlow" = @{
    Title    = "System Interaction Flow"
    Category = "SEQUENCE · INTERACTION"
    Subtitle = "Component interaction detail: SignalR circuit, StateHasChanged coordination, ValidationFilter, parallel Computer Vision, OpenAI caption, Gemini Imagen3, and ConcurrentDictionary bulk slot management."
    Cards    = @(
      @{ Eyebrow="RENDER MODEL"; Dot="coral"; Heading="Interactive Server"; Items=@("SignalR circuit: Client ↔ Server","OnInitializedAsync pre-render","StateHasChanged triggers DOM diff","FileReader base64 before POST") },
      @{ Eyebrow="API LAYER"; Dot="link"; Heading="Validation + Orchestration"; Items=@("ValidationFilter on every request","Computer Vision parallel (par block)","OpenAI chat.completions","Gemini generateContent image-to-image") },
      @{ Eyebrow="BULK CONCURRENCY"; Dot="ink"; Heading="10 Slot Coordination"; Items=@("_slots[0..9] ConcurrentDictionary","No await between slot launches","Task.WhenAll coordination","Incremental StateHasChanged per slot") }
    )
  }
  "SystemInteractionFlow_SIMPLE" = @{
    Title    = "System Interaction Overview"
    Category = "SEQUENCE · OVERVIEW"
    Subtitle = "Simplified 7-message client–API–AI–DB interaction: POST analyze → AI pipeline → render → save → gallery update."
    Cards    = @(
      @{ Eyebrow="ANALYZE"; Dot="coral"; Heading="Client → AI"; Items=@("POST /api/images/analyze","Vision + OpenAI + Gemini","Return ImageAnalysisResponse") },
      @{ Eyebrow="SAVE"; Dot="link"; Heading="Client → DB"; Items=@("POST /api/user-images/save-result","Table Storage upsert","Return SaveImageResponse") },
      @{ Eyebrow="BULK"; Dot="ink"; Heading="10 Parallel Slots"; Items=@("10 concurrent POST /variation","Gemini Imagen3 per slot","Incremental render") }
    )
  }
  "AccessControl_MATRIX" = @{
    Title    = "Access Control Matrix"
    Category = "SECURITY · MASTER"
    Subtitle = "Role-based access control: Anonymous, Dev User, and Prod User mapped to public endpoints, rate-limited AI endpoints, and authenticated endpoints. All AI calls rate-limited at 10 req/min."
    Cards    = @(
      @{ Eyebrow="PUBLIC"; Dot="coral"; Heading="Anonymous Access"; Items=@("/health · /diag · /scalar/v1 — all roles","/login + /dev-login (Dev only) — all roles","AI endpoints: rate limited, no auth required","Anon blocked from all /api/user-images") },
      @{ Eyebrow="AUTHENTICATED"; Dot="link"; Heading="Dev + Prod Users"; Items=@("/api/user-images CRUD — owns only","DELETE ownership check enforced","/api/bulk-generate/prompts — own UserId","All Blazor pages accessible") },
      @{ Eyebrow="RATE LIMITING"; Dot="ink"; Heading="AI Endpoint Policy"; Items=@("Policy: ai-endpoints","10 req/min per user or IP","HTTP 429 + Retry-After on breach","/api/images/analyze + /api/bulk-generate/*") }
    )
  }
  "AccessControl_MATRIX_SIMPLE" = @{
    Title    = "Access Control Overview"
    Category = "SECURITY · OVERVIEW"
    Subtitle = "Four-node access control: Anonymous → public only; Authenticated → public + rate-limited AI + protected endpoints."
    Cards    = @(
      @{ Eyebrow="ANONYMOUS"; Dot="coral"; Heading="Public Routes Only"; Items=@("/health · /diag","API docs at /scalar/v1","Rate-limited AI calls allowed") },
      @{ Eyebrow="AUTHENTICATED"; Dot="link"; Heading="Dev | Prod Users"; Items=@("All public routes","Rate-limited AI endpoints","Own user-images CRUD") },
      @{ Eyebrow="PROTECTION"; Dot="ink"; Heading="Guards"; Items=@("RequireAuthorization on user data","RequireRateLimiting on AI calls","Ownership check on DELETE") }
    )
  }
}

# HTML template with design system
function Get-HtmlTemplate {
  param($title, $category, $subtitle, $mermaidContent, $cards)

  $cardHtml = ""
  foreach ($card in $cards) {
    $dotClass = $card.Dot
    $itemsHtml = ($card.Items | ForEach-Object { "        <li>$_</li>" }) -join "`n"
    $cardHtml += @"
    <div class="card">
      <p class="card-eyebrow">$($card.Eyebrow)</p>
      <div class="card-header">
        <span class="card-dot $dotClass"></span>
        <h3>$($card.Heading)</h3>
      </div>
      <ul>
$itemsHtml
      </ul>
    </div>
"@
  }

  return @"
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>$title · PoRedoImage</title>
  <link href="https://fonts.googleapis.com/css2?family=Instrument+Serif:ital@0;1&family=Geist:wght@400;500;600&family=Geist+Mono:wght@400;500;600&display=swap" rel="stylesheet">
  <script src="https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.min.js"></script>
  <style>
    *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
    :root {
      --paper: #f5f4ed;
      --paper-2: #efeee5;
      --ink: #0b0d0b;
      --muted: #52534e;
      --soft: #65655c;
      --rule: rgba(11,13,11,0.12);
      --rule-strong: rgba(11,13,11,0.25);
      --accent: #f7591f;
      --accent-tint: rgba(247,89,31,0.08);
      --link: #1a70c7;
      --sans: 'Geist', system-ui, sans-serif;
      --serif: 'Instrument Serif', serif;
      --mono: 'Geist Mono', ui-monospace, monospace;
    }
    html { font-size: 16px; }
    body {
      font-family: var(--sans);
      background: var(--paper);
      color: var(--ink);
      min-height: 100vh;
      padding-bottom: 4rem;
    }

    /* Dot pattern background */
    body::before {
      content: '';
      position: fixed;
      inset: 0;
      background-image: radial-gradient(circle, rgba(11,13,11,0.10) 0.9px, transparent 0.9px);
      background-size: 22px 22px;
      opacity: 0.45;
      pointer-events: none;
      z-index: 0;
    }

    * { position: relative; z-index: 1; }

    .page-header {
      max-width: 960px;
      margin: 0 auto;
      padding: 3rem 2rem 1.5rem;
    }
    .eyebrow {
      font-family: var(--mono);
      font-size: 0.6875rem;
      letter-spacing: 0.18em;
      text-transform: uppercase;
      color: var(--muted);
      margin-bottom: 0.5rem;
    }
    .page-title {
      font-family: var(--serif);
      font-size: 1.75rem;
      font-weight: 400;
      letter-spacing: -0.02em;
      line-height: 1.15;
      color: var(--ink);
      margin-bottom: 0.75rem;
    }
    .page-subtitle {
      font-family: var(--sans);
      font-size: 0.9375rem;
      color: var(--soft);
      line-height: 1.6;
      max-width: 64ch;
    }

    .diagram-wrap {
      max-width: 960px;
      margin: 2rem auto;
      padding: 0 2rem;
    }
    .diagram-container {
      background: var(--paper-2);
      border: 1px solid var(--rule);
      border-radius: 8px;
      overflow-x: auto;
      padding: 2rem;
      min-height: 200px;
    }
    .mermaid {
      display: flex;
      justify-content: center;
      font-family: var(--sans) !important;
    }
    .mermaid svg {
      max-width: 100%;
      height: auto;
    }

    .cards-grid {
      max-width: 960px;
      margin: 1.5rem auto 0;
      padding: 0 2rem;
      display: grid;
      grid-template-columns: 1.1fr 1fr 0.9fr;
      gap: 0.875rem;
    }
    .card {
      background: #ffffff;
      border: 1px solid var(--rule);
      border-radius: 6px;
      padding: 1.25rem;
    }
    .card-eyebrow {
      font-family: var(--mono);
      font-size: 0.625rem;
      letter-spacing: 0.16em;
      text-transform: uppercase;
      color: var(--muted);
      margin-bottom: 0.5rem;
    }
    .card-header {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      margin-bottom: 0.625rem;
    }
    .card-dot {
      width: 7px;
      height: 7px;
      border-radius: 50%;
      flex-shrink: 0;
    }
    .card-dot.coral { background: var(--accent); }
    .card-dot.ink { background: var(--ink); }
    .card-dot.link { background: var(--link); }
    .card-dot.muted { background: var(--muted); }
    .card h3 {
      font-family: var(--sans);
      font-size: 0.875rem;
      font-weight: 600;
      color: var(--ink);
    }
    .card ul {
      list-style: none;
      padding: 0;
    }
    .card ul li {
      font-family: var(--sans);
      font-size: 0.8125rem;
      color: var(--soft);
      line-height: 1.6;
      padding: 0.125rem 0 0.125rem 0.875rem;
      position: relative;
    }
    .card ul li::before {
      content: '—';
      position: absolute;
      left: 0;
      color: var(--muted);
      font-size: 0.625rem;
      top: 0.4rem;
    }

    .page-footer {
      max-width: 960px;
      margin: 2.5rem auto 0;
      padding: 1rem 2rem 0;
      border-top: 1px solid var(--rule);
    }
    .footer-text {
      font-family: var(--mono);
      font-size: 0.6875rem;
      letter-spacing: 0.08em;
      color: var(--muted);
    }
  </style>
</head>
<body>
  <header class="page-header">
    <p class="eyebrow">POREDOIMAGE · $category</p>
    <h1 class="page-title">$title</h1>
    <p class="page-subtitle">$subtitle</p>
  </header>

  <div class="diagram-wrap">
    <div class="diagram-container">
      <pre class="mermaid">
$mermaidContent
      </pre>
    </div>
  </div>

  <div class="cards-grid">
$cardHtml
  </div>

  <footer class="page-footer">
    <p class="footer-text">PoRedoImage · $category · April 2026</p>
  </footer>

  <script>
    mermaid.initialize({
      startOnLoad: true,
      theme: 'base',
      themeVariables: {
        primaryColor: '#efeee5',
        primaryTextColor: '#0b0d0b',
        primaryBorderColor: 'rgba(11,13,11,0.20)',
        lineColor: '#52534e',
        secondaryColor: '#f5f4ed',
        tertiaryColor: '#efeee5',
        background: '#f5f4ed',
        mainBkg: '#efeee5',
        nodeBorder: 'rgba(11,13,11,0.20)',
        clusterBkg: '#efeee5',
        clusterBorder: 'rgba(11,13,11,0.18)',
        titleColor: '#0b0d0b',
        edgeLabelBackground: '#f5f4ed',
        attributeBackgroundColorEven: '#f5f4ed',
        attributeBackgroundColorOdd: '#efeee5',
        activationBorderColor: '#f7591f',
        activationBkgColor: 'rgba(247,89,31,0.08)',
        sequenceNumberColor: '#f5f4ed',
        actorBkg: '#efeee5',
        actorBorder: 'rgba(11,13,11,0.25)',
        actorTextColor: '#0b0d0b',
        actorLineColor: '#52534e',
        signalColor: '#52534e',
        signalTextColor: '#0b0d0b',
        labelBoxBkgColor: '#f5f4ed',
        labelBoxBorderColor: 'rgba(11,13,11,0.18)',
        labelTextColor: '#0b0d0b',
        loopTextColor: '#0b0d0b',
        noteBorderColor: 'rgba(11,13,11,0.18)',
        noteBkgColor: '#fff8f0',
        noteTextColor: '#52534e',
        sectionBkgColor: '#efeee5',
        altSectionBkgColor: '#f5f4ed',
        sectionBkgColor2: '#efeee5',
        taskBorderColor: 'rgba(11,13,11,0.20)',
        taskBkgColor: '#efeee5',
        taskTextColor: '#0b0d0b',
        taskTextLightColor: '#52534e',
        taskTextOutsideColor: '#0b0d0b',
        taskTextClickableColor: '#1a70c7',
        activeTaskBorderColor: '#f7591f',
        activeTaskBkgColor: 'rgba(247,89,31,0.12)',
        gridColor: 'rgba(11,13,11,0.08)',
        doneTaskBkgColor: '#d4edda',
        doneTaskBorderColor: '#28a745',
        critBorderColor: '#f7591f',
        critBkgColor: 'rgba(247,89,31,0.08)',
        stateLabelColor: '#0b0d0b',
        stateBkg: '#efeee5',
        labelColor: '#0b0d0b',
        errorBkgColor: 'rgba(212,81,43,0.08)',
        errorTextColor: '#d4512b',
        classText: '#0b0d0b',
        fontFamily: 'Geist, system-ui, sans-serif',
        fontSize: '13px',
      },
      flowchart: {
        htmlLabels: true,
        curve: 'basis',
        padding: 20,
      },
      sequence: {
        actorMargin: 60,
        messageMargin: 35,
        mirrorActors: false,
        useMaxWidth: true,
        diagramMarginX: 20,
        diagramMarginY: 10,
        boxTextMargin: 5,
        noteMargin: 10,
        messageAlign: 'center',
      },
      er: {
        useMaxWidth: true,
      },
      stateDiagram: {
        useMaxWidth: true,
      },
    });
  </script>
</body>
</html>
"@
}

# Process each diagram
$generated = 0
foreach ($name in $diagrams.Keys) {
  $mmdPath = Join-Path $docsDir "$name.mmd"
  $htmlPath = Join-Path $docsDir "$name.html"

  if (-not (Test-Path $mmdPath)) {
    Write-Warning "Missing: $mmdPath"
    continue
  }

  $mmdContent = Get-Content $mmdPath -Raw -Encoding UTF8
  $meta = $diagrams[$name]

  $html = Get-HtmlTemplate `
    -title $meta.Title `
    -category $meta.Category `
    -subtitle $meta.Subtitle `
    -mermaidContent $mmdContent `
    -cards $meta.Cards

  [System.IO.File]::WriteAllText($htmlPath, $html, [System.Text.Encoding]::UTF8)
  Write-Host "✓ $name.html"
  $generated++
}

Write-Host ""
Write-Host "Generated $generated HTML diagrams in: $docsDir"
