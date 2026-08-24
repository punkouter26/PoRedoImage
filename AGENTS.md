# AGENTS.md — Grok rules for this project

> Hand-maintained. The auto-scan could not detect this stack (it looks for
> `package.json` / `pyproject.toml` / `go.mod`; this repo uses a `.slnx` .NET
> solution), so do NOT re-run "integrate/scan" — it will overwrite this with
> a blank generic file.

## Snapshot
- **Name:** PoRedoImage
- **Language:** C# on **.NET 10** (SDK pinned to `10.0.100` in `global.json`)
- **Stack:** ASP.NET Core BFF/API host + **Blazor WebAssembly** SPA, Azure-hosted
- **Solution:** `PoRedoImage.slnx` (XML solution format — there is no `.sln`)
- **Not a Node/Python project.** There is no `package.json`, no `npm start`,
  no `python -m`. Never suggest those commands.

## Run / build / test — use these exact commands (PowerShell)
```powershell
dotnet build PoRedoImage.slnx
dotnet run --project src/PoRedoImage.Web     # http://localhost:4000 | https://localhost:4001
```
Ports 4000/4001 are fixed in `src/PoRedoImage.Web/Properties/launchSettings.json`
and the E2E suites default to 4000 — do not change them.

**To restart the app:** stop the running `dotnet run` (Ctrl+C in its terminal, or
`Get-Process dotnet | Stop-Process`) and re-run the `dotnet run` line above.
There is no watch/reload server and no VS Code "Reload Window" step involved.
`dotnet watch --project src/PoRedoImage.Web` is the hot-reload variant.

```powershell
dotnet test tests/PoRedoImage.Tests.Unit           # pure logic, no I/O, <1s
dotnet test tests/PoRedoImage.Tests.Integration    # needs Docker (Testcontainers/Azurite)
dotnet test tests/PoRedoImage.Tests.E2E.ApiSmoke   # self-skips with no live instance
dotnet test tests/PoRedoImage.Tests.E2E.UI         # Playwright; self-skips with no live instance
dotnet test --filter "FullyQualifiedName~SomeTestClass.SomeMethod"
pwsh ./SCRIPTS/run-e2e.ps1                         # build + mock AI + E2E + teardown
```
CI does **not** run tests — `.github/workflows/deploy.yml` is build/publish/deploy only.

## Running without Azure
No `dotnet user-secrets` (deliberately no `UserSecretsId`). Secrets come from Key
Vault `kv-poshared` via `DefaultAzureCredential`; `az login` is the normal path.
Offline fallback:
```powershell
$env:Mocks__UseMockAi='true'; $env:Storage__ConnectionString=''; dotnet run --project src/PoRedoImage.Web
```

## Layout
- `src/PoRedoImage.Client/` — Blazor WASM SPA. **All interactive UI lives here.**
- `src/PoRedoImage.Mobile/` — .NET MAUI mobile client (Android-first, fast camera intake + minimalist UI).
- `src/PoRedoImage.Web/` — ASP.NET Core BFF/API host. `Components/App.razor` is a
  host document only. Server code is **Vertical Slice** under `Features/`.
- `src/PoRedoImage.Shared/` — DTOs + FluentValidation across the WASM/API boundary; keep trim-safe.
- `src/PoRedoImage.{Domain,Application,Infrastructure}/` — cross-slice primitives only.
- `tests/`, `infra/` (Bicep), `SCRIPTS/` (PowerShell), `DOCS/`.

## Hard rules — these fail review
- New UI goes in `.Client`, never `.Web`. No `RenderMode.InteractiveServer`.
- Auth is BFF: HttpOnly + SameSite=Strict cookies, claims-only principal serialized
  to WASM. **Never** call `AddOidcAuthentication()` — no tokens in the browser.
- Authorization is fail-closed (`FallbackPolicy = RequireAuthenticatedUser`); a new
  endpoint is authenticated unless it explicitly calls `.AllowAnonymous()`.
- Every state-changing endpoint group must call `.RequireAntiforgeryValidation()`.
  Do not also set `RequiresValidation = true`.
- A new feature is a new **slice**, not a new Onion layer. VSA wins.
- Packages are centrally managed in `Directory.Packages.props` — a `PackageReference`
  with an inline version will not build.
- `TreatWarningsAsErrors` + trim analyzer + NuGet audit at `low` are on repo-wide.
  Use `ConfigKeys`/`IOptions<T>`, never raw `Configuration["literal"]`; never
  `ConfigurationBinder.GetValue<T>` (IL2026) — use `ConfigValue`.
- No `Task.Result` / `.Wait()`. Async all the way.
- Zero-Waste: delete dead code in the same change.
- Any new AI-provider fallback path must set a user-facing reason
  (see `RapRoastResponse.DescriptionFallbackReason`) — silent degradation is a bug.

## Deeper reference
`AGENT.MD` (uppercase, separate file) is the living architectural reference and wins
on conflict. `CLAUDE.md` mirrors these rules. `README.md` is a PRD and its test
commands are stale.

## How Grok should work here
1. Prefer **read_file / write_file**; write complete files. Tools save to disk.
2. Read only what the task needs. Ignore `bin/`, `obj/`, `.git/`, `TestResults/`.
3. Match existing style and naming. Keep changes scoped to the request.
4. Never invent API keys, weaken auth/TLS, or commit `.env` values.
5. After tools, summarize paths changed and how to verify.

See `.xgrok/BEST_PRACTICES.md` and `.xgrok/SECURITY.md`.
