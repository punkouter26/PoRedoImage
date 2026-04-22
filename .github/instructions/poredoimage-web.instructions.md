---
description: "Use when working on PoRedoImage.Web"
applyTo: "src/PoRedoImage.Web/**"
---

---
applyTo: "src/PoRedoImage.Web/**"
---

# PoRedoImage.Web — Supplemental Instructions

> Core role, architecture, patterns, logging, auth, and build commands are covered by  
> [poredoimage-web.instructions.md](poredoimage-web.instructions.md). This file adds conventions not captured there.

## Blazor Page Conventions
- All pages under `Components/Pages/` use `@rendermode InteractiveServer`.
- Auth-gated pages carry `@attribute [Authorize]`; public pages carry `@attribute [AllowAnonymous]`.
- `Components/_Imports.razor` globally imports `PoRedoImage.Domain.Entities`, `PoRedoImage.Domain.Interfaces`, and `PoRedoImage.Web.Models` — do not re-declare these per-file.
- `MainLayout` (`Components/Layout/`) places `<ActiveImageBar />` directly after `<header>` and `<RadzenNotification />` before `</body>`. Do **not** add `<RadzenComponents />` here — it lives in the Client WASM layout.

## Image File Handling
- All browser file uploads go through `ImageLoadHelper.LoadAsync(IBrowserFile)` (in `Components/Shared/`).  
  Returns `(LoadResult?, string?)` — always check the error string before using the result.  
  Hard limits: **20 MB** max, **JPEG / PNG only** (validated by extension then magic bytes at the API layer).
- API endpoints validate image magic bytes: JPEG `FF D8 FF`, PNG `89 50 4E 47`. Invalid bytes → HTTP 400.
- Content policy violations from Azure OpenAI (`ClientResultException` with `content_policy_violation`) → HTTP 422, not 500.

## Auto-Save Pattern
Feature pages fire-and-forget saves after successful operations:
```csharp
// After file load — save original to gallery
_ = AutoSaveOriginalAsync(bytes, contentType, fileName);
// After AI result — save result to gallery and refresh the gallery component
_ = AutoSaveResultAsync(bytes, contentType);
```
Failures are caught and logged as `LogWarning` — never bubble to the user.

## JS Interop Functions (wwwroot/js/)
| Function | Signature | Description |
|---|---|---|
| `downloadImage(url, filename)` | `-> bool` | Triggers browser download from a data URL or blob URL |
| `createSideBySideComparison(leftUrl, rightUrl, filename)` | `-> void` | Canvas-composites two images and downloads the result |
| `imageProcessing.resizeToDataUrl(file, maxDimension)` | `-> Promise<string>` | Client-side resize to a JPEG data URL |

Call via `IJSRuntime.InvokeAsync<bool>(...)` / `InvokeVoidAsync(...)`.

## Dev Auth Bypass
`/dev-login?email=X&returnUrl=Y` (Development only) — signs in as any email via cookie.  
`anon@anon.local` is the reserved ANON identity (userId `"anon|ANON"`) used for one-click bypass and E2E tests.

## BulkGenerate Endpoint Groups
`BulkGenerateEndpoints` uses **two separate route groups** on the same `/api/bulk-generate` prefix:
- **Auth group** (`RequireAuthorization`): prompt persistence (`GET/POST /prompts`)
- **AI group** (`RequireRateLimiting("ai-endpoints")`, no auth): generation (`POST /describe`, `POST /variation`)

The `/describe` endpoint calls `IGenerativeAiService.DescribePersonAsync` once per batch; the result feeds `DefaultPrompts.PersonToken` (`"<PERSON>"`) substitution client-side.

## Models/DefaultPrompts
`DefaultPrompts.All` (in `Models/`) holds the 10 default art-style prompts for Bulk Generate.  
Each prompt contains `DefaultPrompts.PersonToken = "<PERSON>"` as a placeholder substituted at generation time with the AI's subject description. Do not hard-code prompt text outside this class.

## Security Conventions
- `GetImageAsync` sanitizes the `id` route param: rejects anything non-hex or over 64 chars before hitting storage.
- `returnUrl` in auth endpoints is validated with `Uri.IsWellFormedUriString(..., Relative)` and rejected if it starts with `//` to prevent open-redirect.

## Program.cs Bootstrap Order
1. Bootstrap Serilog (warnings only) → Key Vault load → full Serilog reconfigure → OpenTelemetry  
2. Key Vault failure: **warning + continue** in Development; **fatal + throw** in all other environments.  
3. Log file path: `logs/poredoimage-.log` (dev) / `/home/LogFiles/Application/poredoimage-.log` (App Service).

## Test Integration Points
- `Program` is declared `public partial class` to support `WebApplicationFactory<Program>`.
- `InternalsVisibleTo` grants access to `PoRedoImage.Tests.Unit` and `PoRedoImage.Tests.Integration`.
- User secrets ID: `PoRedoImage-Web` (for `dotnet user-secrets`).
