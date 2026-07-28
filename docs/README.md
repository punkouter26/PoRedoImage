# PoRedoImage

> AI-powered image studio · art, memes, and bulk variations from a single upload.

## What
PoRedoImage is a Blazor Web App that uses **Azure Computer Vision**, **Azure OpenAI GPT-4.1-nano**, and **Google Gemini Imagen3** to turn any photo into gallery-ready art, captions, or memes. Mobile-first, global Interactive WebAssembly, no prerender. Lives behind an ASP.NET Core BFF; storage is Azure Table Storage; secrets are in Azure Key Vault.

## Who
- **Creatives** — social-media creators who want 10 stylised variations from one photo.
- **Casual users** — anyone wanting a fun meme from a single image.
- **Developers** — engineers integrating the API; rely on `/scalar/v1`, `/health`, `/diag`.

## Why
The promise is **upload → choose style → result in < 10 seconds**. The vertical-slice layout of the server makes it cheap to add new image transforms. The BFF-with-HttpOnly-cookie model means production OIDC works for any M365 tenant without putting tokens in the browser.

## Local setup (bare-metal, ~5 min)

Requires **Windows 10/11, PowerShell 7+, Docker, Winget**.

```powershell
# One-time, idempotent setup
pwsh -File SCRIPTS/setup.ps1

# Then run
dotnet run --project src/PoRedoImage.Web
# → http://localhost:4000  |  https://localhost:4001
# Dev sign-in: open /dev-login?email=you@example.com (cookie login)
# or click "Continue as GUEST" (Development only).
```

## Architecture report (generated)

Open [`ARCHITECTURE_REPORT.html`](ARCHITECTURE_REPORT.html) in a browser — a single-file dashboard
with the slice matrix, diagnostic charts, and a zero-waste audit.

| Doc | Purpose |
|---|---|
| [`ARCHITECTURE_REPORT.html`](ARCHITECTURE_REPORT.html) | Interactive dashboard: slice matrix, live-rendered Mermaid, diagnostics, zero-waste audit |
| [`AI_MODELS.html`](AI_MODELS.html) | Which model serves which task, and which are **verified working** from the app log |
| [`CHANGELOG_SIMPLE.html`](CHANGELOG_SIMPLE.html) / [`CHANGELOG_DETAILED.html`](CHANGELOG_DETAILED.html) | Recent changes with +/- line counts, reasoning, and key snippets |
| [`ROLES_MATRIX_SIMPLE.html`](ROLES_MATRIX_SIMPLE.html) / [`ROLES_MATRIX_DETAILED.html`](ROLES_MATRIX_DETAILED.html) | Identity types, the anonymous surface, and per-endpoint access |
| [`diagnostic_history.json`](diagnostic_history.json) | Web-vitals sample store consumed by the dashboard (**empty** — no collector exists yet) |
| [`diagrams/`](diagrams/) | 6 diagrams × simple/detailed, each as `.mmd` + `.svg` + standalone `.html` |

Regenerate the diagram SVG/HTML after editing any `.mmd`:

```powershell
pwsh -File docs/diagrams/_render.ps1 -Force
```

## Documentation index (hand-written)

| Doc | Purpose |
|---|---|
| [`AGENT.md`](AGENT.md)               | Foundational context for AI coding agents |
| [`PRD_Master.md`](PRD_Master.md)     | Source of truth: API, slices, data, contracts |
| [`User_Journey.mmd`](User_Journey.mmd) | Mobile portrait user journey with perf scores |
| [`User_Journey_simplified.mmd`](User_Journey_simplified.mmd) | Simplified journey view |
| [`UI_Screen_Matrix.mmd`](UI_Screen_Matrix.mmd) | Client routes, layout flash mitigations |
| [`UI_Screen_Matrix_simplified.mmd`](UI_Screen_Matrix_simplified.mmd) | Simplified view |
| [`Flow_Identity_BFF.mmd`](Flow_Identity_BFF.mmd) | Entra OIDC, BFF cookie loop, `/auth/me` |
| [`Flow_Identity_BFF_simplified.mmd`](Flow_Identity_BFF_simplified.mmd) | Simplified view |
| [`Flow_Validation_Failures.mmd`](Flow_Validation_Failures.mmd) | UI validation → backend exceptions |
| [`Flow_Validation_Failures_simplified.mmd`](Flow_Validation_Failures_simplified.mmd) | Simplified view |
| [`Flow_RealTime_Lobby.mmd`](Flow_RealTime_Lobby.mmd) | SignalR Hub lifecycle |
| [`Flow_RealTime_Lobby_simplified.mmd`](Flow_RealTime_Lobby_simplified.mmd) | Simplified view |
| [`Architecture_VSA_Blueprint.mmd`](Architecture_VSA_Blueprint.mmd) | VSA topology across Web / Client / Shared |
| [`Architecture_VSA_Blueprint_simplified.mmd`](Architecture_VSA_Blueprint_simplified.mmd) | Simplified view |
| [`Interaction_Trace.mmd`](Interaction_Trace.mmd) | End-to-end request trace via BFF |
| [`Interaction_Trace_simplified.mmd`](Interaction_Trace_simplified.mmd) | Simplified view |
| [`DatabaseSchema.mmd`](DatabaseSchema.mmd) | Table Storage entities, PK/RK, indexes |
| [`DatabaseSchema_simplified.mmd`](DatabaseSchema_simplified.mmd) | Simplified view |

## Render the diagrams

```powershell
Get-ChildItem docs/*.mmd | ForEach-Object {
    $out = [System.IO.Path]::ChangeExtension($_.FullName, "svg")
    npx @mermaid-js/mermaid-cli -i $_.FullName -o $out | Out-Null
    Write-Host "✔ $($_.Name) → $(Split-Path $out -Leaf)"
}
```