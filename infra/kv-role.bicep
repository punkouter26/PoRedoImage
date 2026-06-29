// ─── PoShared Key Vault role assignment (subscription-scoped ORCHESTRATOR) ───
// Grants an App Service's system-assigned managed identity the
// "Key Vault Secrets User" role on the shared Key Vault (kv-poshared in
// the PoShared resource group). The web app's App Settings use
// @Microsoft.KeyVault(VaultName=...;SecretName=...) references that the
// platform resolves via the managed identity; without this role every
// App Setting resolves to an empty string and the app fails its
// StartupSecretValidator in Production.
//
// Why a Bicep module instead of inline `az role assignment create`:
//   - Idempotent: Bicep is aware of the existing assignment; re-runs are clean
//     (no `grep -v AlreadyExists` shell hack in the workflow).
//   - What-ifable: `az deployment sub what-if --template-file infra/kv-role.bicep`
//     previews the change before applying.
//   - Reusable across Po* projects (PoFace, PoNovaWeight, etc. — all consume
//     the same shared Key Vault).
//   - Auditable: the role definition id, scope, and principal type are
//     version-controlled instead of buried in a shell string.
//
// The actual role assignment is in modules/kv-role-assignment.bicep because
// Bicep forbids declaring a roleAssignment at a different scope from the file's
// own targetScope (BCP139). This file is the subscription-scoped orchestrator;
// the module is resourceGroup-scoped to the Key Vault's RG.
//
// Usage (called from .github/workflows/deploy.yml after infra/main.bicep applies):
//   az deployment sub create \
//     --location westus2 \
//     --template-file infra/kv-role.bicep \
//     --parameters principalId=<webapp.principalId>
// ─────────────────────────────────────────────────────────────────────────────

targetScope = 'subscription'

@description('Principal ID (objectId) of the App Service managed identity that needs Key Vault Secrets User')
param principalId string

@description('Name of the Key Vault in the shared resource group')
param keyVaultName string = 'kv-poshared'

@description('Resource group containing the Key Vault')
param keyVaultResourceGroup string = 'PoShared'

module kvRole 'modules/kv-role-assignment.bicep' = {
  name: 'kv-role-${uniqueString(principalId, keyVaultName)}'
  scope: resourceGroup(keyVaultResourceGroup)
  params: {
    keyVaultName: keyVaultName
    principalId: principalId
  }
}

output roleAssignmentId string = kvRole.outputs.roleAssignmentId
output keyVaultId string = kvRole.outputs.keyVaultId
