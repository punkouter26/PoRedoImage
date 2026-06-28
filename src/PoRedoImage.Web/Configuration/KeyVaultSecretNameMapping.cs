using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Security.KeyVault.Secrets;

namespace PoRedoImage.Web.Configuration;

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
        ["PoRedoImage-ComputerVision-ApiKey"] = "ComputerVision:ApiKey",
        ["PoRedoImage-ComputerVision-Endpoint"] = "ComputerVision:Endpoint",
        ["PoRedoImage-OpenAI-ApiKey"] = "OpenAI:Key",
        ["PoRedoImage-OpenAI-Endpoint"] = "OpenAI:Endpoint",
        // NOTE: the chat deployment NAME is intentionally NOT sourced from Key Vault. It is not a
        // secret, and a stale KV copy (gpt-4.1-nano) previously shadowed the live value and caused
        // 404 DeploymentNotFound. The single source of truth is now config: appsettings.json for
        // local/dev/test, overridden by the literal OpenAI__ChatCompletionsDeployment app setting in
        // infra/main.bicep for Production.
        ["PoRedoImage-ApplicationInsights-ConnectionString"] = "ApplicationInsights:ConnectionString",
        ["PoRedoImage-StorageConnectionString"] = "Storage:ConnectionString",
        ["PoRedoImage-AzureAd-ClientId"] = "AzureAd:ClientId",
        ["PoRedoImage-AzureAd-ClientSecret"] = "AzureAd:ClientSecret",
        ["PoRedoImage-Google-ApiKey"] = "Google:ApiKey",
        ["PoRedoImage-Google-Imagen3Model"] = "Google:Imagen3Model"
    };

    public static IReadOnlyCollection<string> RequiredSecretNames => SecretMappings.Keys;

    public static IReadOnlyCollection<string> RequiredConfigurationKeys => SecretMappings.Values;

    public override bool Load(SecretProperties secret) =>
        SecretMappings.ContainsKey(secret.Name);

    public override string GetKey(KeyVaultSecret secret) =>
        SecretMappings.TryGetValue(secret.Name, out var configKey)
            ? configKey
            : secret.Name.Replace("--", ":");
}
