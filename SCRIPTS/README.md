# SCRIPTS

This folder contains developer utility scripts for the PoRedoImage project.

| Script | Type | Purpose |
|--------|------|---------|
| _(none yet)_ | — | Add scripts here as needed |

## Conventions

- PowerShell scripts (`.ps1`) for Windows automation and Azure CLI tasks.
- Python scripts (`.py`) for data processing or cross-platform utilities.
- Each script must have a comment block at the top explaining its purpose, prerequisites, and usage.

## Common Tasks

### Local Development Setup

Start Azurite (Azure Storage emulator) via Docker Compose before running the app locally:

```powershell
docker compose -f ../docker-compose.azurite.yml up -d
```

### Running Tests

```powershell
# Unit + Integration tests with coverage
dotnet test ../PoRedoImage.slnx --collect:"XPlat Code Coverage" --results-directory ../TestResults

# E2E tests (requires the app to be running on http://localhost:5000)
npx playwright test --config=../tests/playwright/playwright.config.ts
```
