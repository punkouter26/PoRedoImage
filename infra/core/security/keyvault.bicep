@description('The name of the Key Vault')
param name string

@description('The location of the Key Vault')
param location string = resourceGroup().location

@description('The tags to apply to the Key Vault')
param tags object = {}

@description('The Azure AD tenant ID')
param tenantId string

@description('The principal ID of the managed identity to grant access')
param principalId string

@description('Enable purge protection to prevent permanent deletion')
param enablePurgeProtection bool = true

@description('Soft delete retention period in days')
param softDeleteRetentionInDays int = 7

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: tenantId
    enableRbacAuthorization: true // Use RBAC instead of access policies
    enableSoftDelete: true
    softDeleteRetentionInDays: softDeleteRetentionInDays
    enablePurgeProtection: enablePurgeProtection
    publicNetworkAccess: 'Enabled' // Can be restricted later for production
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Allow' // Can be changed to 'Deny' with specific IP rules
    }
  }
}

// Built-in Azure RBAC role: Key Vault Secrets User
// https://learn.microsoft.com/en-us/azure/role-based-access-control/built-in-roles#key-vault-secrets-user
var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

// Grant the managed identity read access to secrets
resource secretsUserRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, principalId, keyVaultSecretsUserRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}

@description('The ID of the Key Vault')
output id string = keyVault.id

@description('The name of the Key Vault')
output name string = keyVault.name

@description('The URI of the Key Vault')
output vaultUri string = keyVault.properties.vaultUri
