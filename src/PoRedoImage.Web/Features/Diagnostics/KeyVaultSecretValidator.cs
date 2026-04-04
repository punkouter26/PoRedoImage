using Azure;
using Azure.Security.KeyVault.Secrets;
using Serilog;

namespace PoRedoImage.Web.Features.Diagnostics;

public static class KeyVaultSecretValidator
{
    /// <summary>
    /// Confirms all secrets expected by the app mapping exist in Key Vault and are not empty.
    /// Throws InvalidOperationException if any missing/empty values are found,
    /// so app startup fails loudly instead of running with incomplete config.
    /// </summary>
    public static void ValidateRequiredSecrets(SecretClient client, Serilog.ILogger logger)
    {
        var missing = new List<string>();

        foreach (var secretName in KeyVaultSecretNameMapping.RequiredSecretNames)
        {
            try
            {
                var secret = client.GetSecret(secretName);
                if (string.IsNullOrWhiteSpace(secret.Value.Value))
                {
                    missing.Add(secretName);
                }
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                missing.Add(secretName);
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Unable to read Key Vault secret '{SecretName}'", secretName);
                missing.Add(secretName);
            }
        }

        if (missing.Any())
        {
            var message = "Missing required Key Vault secrets: " + string.Join(", ", missing);
            logger.Error(message);
            throw new InvalidOperationException(message);
        }

        logger.Information("All required Key Vault secrets were successfully loaded from vault {KeyVaultUri}", client.VaultUri);
    }
}
