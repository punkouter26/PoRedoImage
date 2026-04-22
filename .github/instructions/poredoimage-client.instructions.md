---
description: "Use when working on PoRedoImage.Client"
applyTo: "src/PoRedoImage.Client/**"
---

---
description: "Use when working on PoRedoImage.Client"
applyTo: "src/PoRedoImage.Client/**"
---

# PoRedoImage.Client — Area Instructions

## Role
Blazor **WebAssembly** client project. It is loaded as an additional assembly by the `PoRedoImage.Web` host (see [poredoimage-web.instructions.md](poredoimage-web.instructions.md)) and is **not** run standalone in production. Its primary purpose is to house WASM-only components and the client-side entry point (`Program.cs`).

## Directory Layout
```
Layout/
  MainLayout.razor     # Root layout — contains <RadzenComponents />
  NavMenu.razor        # Collapsible sidebar nav
Pages/                 # (currently empty) WASM-rendered page components go here
wwwroot/
  index.html           # WASM bootstrap host page (standalone dev only)
  css/app.css          # Global styles + CSS design tokens
  lib/bootstrap/       # Bootstrap 5 distribution (static, do not edit)
Program.cs             # WASM entry point — DI wiring
_Imports.razor         # Global using directives for all .razor files
App.razor              # Router root
```

> Most feature pages live in `PoRedoImage.Web/Components/Pages/` and use `InteractiveServer` render mode. Add new pages here only when they require **pure WASM** rendering (`@rendermode InteractiveWebAssembly`).

## DI & HttpClient
`Program.cs` registers a single scoped `HttpClient` pointed at `builder.HostEnvironment.BaseAddress`:

```csharp
builder.Services.AddScoped(sp =>
    new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
```

- Inject `HttpClient` (not `IHttpClientFactory`) in WASM components — there is only one named client.
- Call server API endpoints via relative paths (e.g., `"/api/images/analyze"`).
- For DTO contracts used in `HttpClient.GetFromJsonAsync` / `PostAsJsonAsync`, use types from `PoRedoImage.Shared.DTOs` — see [poredoimage-shared.instructions.md](poredoimage-shared.instructions.md).

## Radzen UI
Radzen.Blazor is the project's component library.

| Concern | Convention |
|---|---|
| Registration | `builder.Services.AddRadzenComponents()` in `Program.cs` — covers Dialog, Notification, Tooltip, ContextMenu |
| Root layout | `<RadzenComponents />` **must** remain at the bottom of `MainLayout.razor`; removing it breaks all overlay services |
| Theme | Radzen **Material** base (`_content/Radzen.Blazor/css/material-base.css` loaded in `index.html`) |
| Notifications | Inject `NotificationService`; call `ShowSuccess` / `ShowError` — do not use JavaScript `alert()` |
| Dialogs | Inject `DialogService`; use `OpenAsync<TComponent>(...)` for confirmations and modal forms |

All Radzen namespaces (`Radzen`, `Radzen.Blazor`) are already globally imported via `_Imports.razor` — no per-file `@using` needed.

## Global Imports (`_Imports.razor`)
The following are available in every `.razor` file without an explicit `@using`:

- `System.Net.Http`, `System.Net.Http.Json`
- `Microsoft.AspNetCore.Components.*` (Forms, Routing, Web, Web.Virtualization, WebAssembly.Http)
- `Microsoft.JSInterop`
- `PoRedoImage.Client`, `PoRedoImage.Client.Layout`
- `PoRedoImage.Shared.DTOs`
- `Radzen`, `Radzen.Blazor`

Do **not** duplicate these in individual components.

## CSS & Design Tokens
`wwwroot/css/app.css` owns the client-side global styles and defines CSS custom properties that **mirror** the Web project's token set:

```css
:root {
  --color-primary:      #052767;
  --color-primary-end:  #3a0647;
  --color-primary-grad: linear-gradient(135deg, #052767 0%, #3a0647 100%);
  --color-bg:           #f8fafc;
  --color-body:         #1e293b;
  --color-muted:        #64748b;
}
```

- **Always use these tokens** (e.g., `var(--color-primary)`) for colors in new styles; never hard-code hex values that duplicate a token.
- If you add a new token here, add the identical token to `PoRedoImage.Web/wwwroot/app.css` to keep both surfaces consistent.
- Scoped component styles go in co-located `.razor.css` files; global utility classes go in `app.css`.

## Layout Conventions
- `MainLayout` uses a two-column layout: `.sidebar` (contains `<NavMenu />`) + `<main>` (contains `@Body`).
- `NavMenu` toggles collapse state via a boolean field; the CSS class `collapse` controls visibility — do not replace with JavaScript-driven show/hide.
- New top-level nav entries: add a `<div class="nav-item px-3">` with a `<NavLink>` and a matching Bootstrap icon `<span>`.

## Namespace
- Root namespace: `PoRedoImage.Client`
- Layout components: `PoRedoImage.Client.Layout`
- Page components (if added): `PoRedoImage.Client.Pages`

## Build & Run
The Client project is not meant to run independently in production; use `PoRedoImage.Web` as the host. For standalone WASM development only:
```bash
dotnet run --project src/PoRedoImage.Client
# Dev server: https://localhost:7285 / http://localhost:5112
```

`Nullable` and `ImplicitUsings` are enabled; treat all nullable warnings as errors before committing.
