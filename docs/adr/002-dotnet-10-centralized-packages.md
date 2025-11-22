# ADR 002: Migrate to .NET 10 and Centralized Package Management

**Status:** Accepted  
**Date:** 2025-11-22  
**Deciders:** Development Team  

## Context

The application was originally built with .NET 9 and used per-project package version management. This led to:
- Version inconsistencies across projects
- Difficult dependency upgrades
- No clear view of all dependencies
- Potential security vulnerabilities from outdated packages

## Decision

Migrate to **.NET 10** with **Centralized Package Management** (CPM) using `Directory.Packages.props`.

### Implementation Details

1. **SDK Version Lock:**
   - Created `global.json` specifying SDK 10.0.100
   - `rollForward: latestPatch` for automatic patch updates
   - Prevents build issues from SDK version drift

2. **Centralized Package Management:**
   - All package versions defined in `Directory.Packages.props` at repository root
   - Projects reference packages without version numbers
   - Single source of truth for all dependencies

3. **Package Organization:**
   ```xml
   <PackageVersion Include="Azure.AI.OpenAI" Version="2.1.0" />
   ```
   - Grouped by category (Azure SDKs, Microsoft Core, Testing, etc.)
   - Easy to scan and upgrade

## Consequences

### Positive

- ✅ **Consistency:** All projects use same package versions
- ✅ **Maintainability:** Update versions in one place
- ✅ **Security:** Easier to audit and update vulnerable packages
- ✅ **Build Reliability:** Locked SDK prevents version drift
- ✅ **Clarity:** Clear view of all dependencies
- ✅ **Latest Features:** Access to .NET 10 improvements

### Negative

- ❌ **Breaking Changes:** Required code updates for API changes
- ❌ **Migration Effort:** One-time effort to restructure projects
- ❌ **Learning:** Team needs to understand CPM concepts

### Mitigations

- Comprehensive testing after migration
- Build warnings treated as errors
- Dependency vulnerability scanning enabled

## Migration Steps

1. Created `global.json` with SDK 10.0.100
2. Created `Directory.Packages.props` with all package versions
3. Updated all `.csproj` files to remove version numbers
4. Updated target frameworks from `net9.0` to `net10.0`
5. Resolved build errors and warnings
6. Verified all tests pass

## Alternatives Considered

### 1. Stay on .NET 9
**Rejected:** Missing latest features and performance improvements

### 2. Per-Project Version Management
**Rejected:** Continues version inconsistency problem

### 3. NuGet.config with version pinning
**Rejected:** Not as explicit as CPM, harder to maintain

## References

- [Central Package Management](https://learn.microsoft.com/nuget/consume-packages/central-package-management)
- [global.json overview](https://learn.microsoft.com/dotnet/core/tools/global-json)
- [.NET 10 What's New](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-10)

## Related Decisions

- ADR 003: Use FluentValidation in Shared Project
- ADR 004: Standardize on OpenTelemetry for Metrics
