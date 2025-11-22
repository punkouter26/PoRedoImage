# ADR 001: Use Azure Key Vault for Secret Management

**Status:** Accepted  
**Date:** 2025-11-22  
**Deciders:** Development Team  

## Context

The PoRedoImage application requires secure management of sensitive configuration data including:
- Azure Computer Vision API keys
- Azure OpenAI API keys  
- Application Insights connection strings
- Azure Table Storage connection strings

Previously, secrets were stored directly in appsettings.json files or environment variables, creating security risks and making secret rotation difficult.

## Decision

We will use **Azure Key Vault** for production secret storage and **dotnet user-secrets** for local development.

### Implementation Details

1. **Production Environment:**
   - All secrets stored in Azure Key Vault
   - App Service uses Managed Identity for passwordless authentication
   - Key Vault uses RBAC (Role-Based Access Control) instead of access policies
   - Soft delete and purge protection enabled
   - Program.cs reads from Key Vault only when `ASPNETCORE_ENVIRONMENT=Production`

2. **Local Development:**
   - Secrets stored using `dotnet user-secrets` (encrypted on disk)
   - No secrets in source control
   - appsettings.Development.json uses Azurite for storage emulation

3. **Secret Naming:**
   - Key Vault: Use `--` for hierarchy (e.g., `OpenAI--Key`)
   - User Secrets: Use `:` for hierarchy (e.g., `OpenAI:Key`)
   - .NET configuration automatically converts `--` to `:` when reading

## Consequences

### Positive

- ✅ **Security:** Secrets never committed to source control
- ✅ **Centralization:** Single source of truth for production secrets
- ✅ **Audit Trail:** Key Vault logs all secret access
- ✅ **Rotation:** Easy secret rotation without code deployment
- ✅ **Access Control:** Fine-grained RBAC permissions
- ✅ **Developer Experience:** user-secrets makes local dev easy
- ✅ **Cost Efficient:** Free for first 10,000 operations/month

### Negative

- ❌ **Complexity:** Additional infrastructure component
- ❌ **Dependency:** Application requires Key Vault availability in production
- ❌ **Initial Setup:** Requires PowerShell scripts for secret population
- ❌ **Learning Curve:** Team needs to understand Key Vault concepts

### Mitigations

- Created PowerShell scripts to automate secret configuration
- Comprehensive documentation in `docs/SECRETS.md`
- Bicep templates handle Key Vault provisioning automatically
- Fall back to environment variables if Key Vault unavailable

## Alternatives Considered

### 1. Environment Variables Only
**Rejected:** Difficult to manage, no audit trail, secrets visible in portal

### 2. Azure App Configuration
**Rejected:** More expensive, overkill for our use case, still requires Key Vault references

### 3. Managed Identities with Direct Service Auth
**Rejected:** Not all Azure services support it, doesn't solve configuration centralization

## References

- [Azure Key Vault Documentation](https://learn.microsoft.com/azure/key-vault/)
- [Safe storage of app secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets)
- [DefaultAzureCredential](https://learn.microsoft.com/dotnet/api/azure.identity.defaultazurecredential)

## Related Decisions

- ADR 002: Use Managed Identity for Azure Service Authentication
- ADR 003: Implement RBAC on Key Vault (not Access Policies)
