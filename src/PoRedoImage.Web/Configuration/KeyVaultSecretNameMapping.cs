using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Security.KeyVault.Secrets;
using PoRedoImage.Shared.Configuration;

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
        ["PoRedoImage-ComputerVision-ApiKey"] = ConfigKeys.ComputerVisionApiKey,
        ["PoRedoImage-ComputerVision-Endpoint"] = ConfigKeys.ComputerVisionEndpoint,
        ["PoRedoImage-OpenAI-ApiKey"] = ConfigKeys.OpenAiKey,
        ["PoRedoImage-OpenAI-Endpoint"] = ConfigKeys.OpenAiEndpoint,
        // NOTE: the chat deployment NAME is intentionally NOT sourced from Key Vault. It is not a
        // secret, and a stale KV copy (gpt-4.1-nano) previously shadowed the live value and caused
        // 404 DeploymentNotFound. The single source of truth is now config: appsettings.json for
        // local/dev/test, overridden by the literal OpenAI__ChatCompletionsDeployment app setting in
        // infra/main.bicep for Production.
        ["PoRedoImage-ApplicationInsights-ConnectionString"] = ConfigKeys.ApplicationInsightsConnectionString,
        ["PoRedoImage-StorageConnectionString"] = ConfigKeys.StorageConnectionString,
        ["PoRedoImage-AzureAd-ClientId"] = ConfigKeys.AzureAdClientId,
        ["PoRedoImage-AzureAd-ClientSecret"] = ConfigKeys.AzureAdClientSecret,
        ["PoRedoImage-Google-ApiKey"] = ConfigKeys.GoogleApiKey,
        // PoRedoImage-HuggingFace-ApiKey was removed in 2026-08 with the provider. It is no longer
        // required at startup, so StartupSecretValidator will not fail if it is absent from the
        // vault — deleting the secret itself is a separate, optional cleanup.
        ["PoRedoImage-Google-Imagen3Model"] = ConfigKeys.GoogleImagen3Model
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
