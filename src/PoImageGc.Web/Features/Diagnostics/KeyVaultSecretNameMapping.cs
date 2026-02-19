using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Security.KeyVault.Secrets;

namespace PoImageGc.Web.Features.Diagnostics;

/// <summary>
/// Maps Key Vault secret names to .NET configuration keys.
/// Implements the Adapter pattern to bridge Key Vault naming convention
/// (e.g., "PoRedoImage-ComputerVision-ApiKey") with .NET configuration keys
/// (e.g., "ComputerVision:ApiKey"). All secrets use the "PoRedoImage-" prefix
/// to namespace them within the shared PoShared Key Vault.
/// </summary>
public class KeyVaultSecretNameMapping : KeyVaultSecretManager
{
    private static readonly Dictionary<string, string> SecretMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PoRedoImage-ComputerVision-ApiKey"]             = "ComputerVision:ApiKey",
        ["PoRedoImage-ComputerVision-Endpoint"]           = "ComputerVision:Endpoint",
        ["PoRedoImage-OpenAI-ApiKey"]                     = "OpenAI:Key",
        ["PoRedoImage-OpenAI-Endpoint"]                   = "OpenAI:Endpoint",
        ["PoRedoImage-OpenAI-DeploymentName"]             = "OpenAI:ChatCompletionsDeployment",
        ["PoRedoImage-OpenAI-ImageEndpoint"]              = "OpenAI:ImageEndpoint",
        ["PoRedoImage-OpenAI-ImageKey"]                   = "OpenAI:ImageKey",
        ["PoRedoImage-ApplicationInsights-ConnectionString"] = "ApplicationInsights:ConnectionString",
        ["PoRedoImage-StorageConnectionString"]           = "Storage:ConnectionString"
    };

    public override bool Load(SecretProperties secret) =>
        SecretMappings.ContainsKey(secret.Name);

    public override string GetKey(KeyVaultSecret secret) =>
        SecretMappings.TryGetValue(secret.Name, out var configKey)
            ? configKey
            : secret.Name.Replace("--", ":");
}
