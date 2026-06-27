# SCRIPTS

This folder contains developer utility scripts for the PoRedoImage project.

| Script | Type | Purpose |
|--------|------|---------|
| `setup.ps1` | PowerShell | One-command machine setup: Winget installs, Docker/Azurite init, package restore |

## Conventions

- PowerShell scripts (`.ps1`) for Windows automation and Azure CLI tasks.
- Python scripts (`.py`) for data processing or cross-platform utilities.
- Each script must have a comment block at the top explaining its purpose, prerequisites, and usage.

## Quick Start (New Machine)

```powershell
# Clone the repo, then run from the repo root:
.\SCRIPTS\setup.ps1
```

This will:
1. Install .NET 10 SDK, Docker Desktop, Git, and Node.js via **Winget**
2. Start **Azurite** (Azure Storage emulator) via Docker Compose
3. Restore all NuGet packages
4. Verify Azure CLI + Key Vault access

After setup, press **F5** in VS Code to build and launch the app.

## Common Tasks

### Start Azurite only

```powershell
docker compose -f ../docker-compose.yml up -d
```

### Running Tests

```powershell
# Unit + Integration tests with coverage
dotnet test ../PoRedoImage.slnx --collect:"XPlat Code Coverage" --results-directory ../TestResults

# E2E tests (C# Playwright + HTTP smoke, requires the app to be running on http://localhost:5000)
dotnet test ../tests/PoRedoImage.Tests.E2E
# First time only: install Playwright browsers
pwsh ../tests/PoRedoImage.Tests.E2E/bin/Release/net10.0/playwright.ps1 install
```

