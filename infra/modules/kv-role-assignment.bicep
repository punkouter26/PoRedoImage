// ─── PoShared Key Vault role assignment (resourceGroup-scoped MODULE) ────────
// Internal module — deployed by infra/kv-role.bicep with an explicit
// `scope: resourceGroup(keyVaultResourceGroup)`. Bicep requires a
// resourceGroup-scoped file for cross-RG role assignments (BCP139).
// ─────────────────────────────────────────────────────────────────────────────

targetScope = 'resourceGroup'

@description('Name of the existing Key Vault in this resource group')
param keyVaultName string

@description('Principal ID (objectId) of the App Service managed identity')
param principalId string

// Built-in role definition id for "Key Vault Secrets User" (data plane read).
// Source: https://learn.microsoft.com/azure/role-based-access-control/built-in-roles#key-vault-secrets-user
var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

// Stable GUID name so re-runs replace the same assignment (no duplicates).
resource kvRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, principalId, keyVaultSecretsUserRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}

output roleAssignmentId string = kvRoleAssignment.id
output keyVaultId string = keyVault.id
