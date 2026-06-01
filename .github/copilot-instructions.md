# PoRedoImage — Copilot Skills & Instructions

This file defines reusable Copilot skills for PoRedoImage development. Each skill is a prompt template you can invoke when performing specific tasks.

## Phase 1 — Understand the Codebase

### `acquire-codebase-knowledge`
Map everything before touching a line. Generate STACK, ARCHITECTURE, CONVENTIONS, TESTING docs. Skip if starting from zero.

## Phase 2 — Design & Plan

### `architecture-blueprint-generator`
Define your layers, patterns, and component boundaries. Becomes the blueprint every future decision references.

### `folder-structure-blueprint-generator`
Translate the architecture into a concrete folder layout. Establishes where every file type lives before you create any.

## Phase 3 — Build Features

### `dotnet-best-practices`
Apply while writing any C# class — DI, async, error handling, configuration patterns.

### `dotnet-design-pattern-review`
Review code after writing a service or domain object. Catches pattern violations before they compound.

### `autoresearch`
Use when you need to iteratively optimize something measurable — test pass rate, response time, build size. Runs a full experiment loop autonomously.

## Phase 6 — Harden & Secure

### `security-review`
Full OWASP scan before merging to main. Catches secrets, data-flow vulnerabilities, and injection risks across the whole changeset.

## Phase 7 — Observability

### `appinsights-instrumentation`
Wire up telemetry before you go live. Useless to add after an incident — you want data from day one.

## Phase 8 — Deploy

### `azure-deployment-preflight`
Run immediately before `azd up`. Validates Bicep syntax, previews what-if changes, checks permissions. Saves costly rollbacks.

## Phase 9 — Operate

### `azure-resource-health-diagnose`
Triggered reactively when something breaks in Azure, or proactively on a schedule. Queries logs, classifies issues, generates a remediation plan.

## Phase 10 — Document

### `create-readme`
Write the README once the project is stable enough to describe accurately.

### `repo-story-time`
End of a release or major milestone. Mines git history and generates REPOSITORY_SUMMARY.md + a narrative story of the project's evolution.

---

## Engineering Standards

### C# 14 + .NET 10
- Use latest C# features (primary constructors, collection expressions, etc.)
- Follow Onion Architecture: Domain → Application → Infrastructure → Web
- All warnings as errors, nullable enabled globally
- XML `<remarks>` tags explaining *why* a pattern was used

### Blazor WASM + Radzen
- No AOT (interpreted mode for smaller builds)
- Radzen UI for complex controls over custom thin logic
- Mobile-first with CSS clamp() and auto-fit

### Storage
- Azurite (Docker) locally, Table Storage in Azure
- Strongly typed DTOs implementing ITableEntity
- Auto-toggle between Azurite and production via configuration

### Auth
- GUEST mode (dev/test only): GUEST + 8 random digits
- Microsoft OIDC (prod): Azure CLI registration required
- GUEST button hidden/disabled in production
- LocalStorage persistence for GUEST sessions

### Testing
- Unit: xUnit + Moq (test data, non-AI)
- Integration: Testcontainers (Azurite)
- E2E: Playwright (TypeScript)
- Dev env uses real AI data; test env uses mock data