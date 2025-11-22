# Architecture Decision Records (ADRs)

This directory contains Architecture Decision Records (ADRs) for the PoRedoImage project. ADRs document significant architectural decisions and their context, consequences, and alternatives.

## Format

Each ADR follows this structure:
- **Title:** Brief description of the decision
- **Status:** Proposed | Accepted | Deprecated | Superseded
- **Date:** When the decision was made
- **Context:** Problem or opportunity being addressed
- **Decision:** What we decided to do
- **Consequences:** Positive and negative outcomes
- **Alternatives Considered:** Other options we evaluated

## Index

### Infrastructure & Security

| ADR | Title | Status | Date |
|-----|-------|--------|------|
| [001](001-use-azure-key-vault.md) | Use Azure Key Vault for Secret Management | Accepted | 2025-11-22 |

### Technology Stack

| ADR | Title | Status | Date |
|-----|-------|--------|------|
| [002](002-dotnet-10-centralized-packages.md) | Migrate to .NET 10 and Centralized Package Management | Accepted | 2025-11-22 |
| [003](003-opentelemetry-observability.md) | Implement OpenTelemetry for Observability | Accepted | 2025-11-22 |

## Creating a New ADR

1. Copy the template: `cp template.md XXX-your-title.md`
2. Fill in all sections
3. Submit for review
4. Update this index
5. Commit to repository

## References

- [ADR GitHub Organization](https://adr.github.io/)
- [Michael Nygard's ADR Template](https://github.com/joelparkerhenderson/architecture-decision-record)
