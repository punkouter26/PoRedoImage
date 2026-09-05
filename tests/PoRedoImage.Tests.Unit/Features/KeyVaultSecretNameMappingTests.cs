using Azure.Security.KeyVault.Secrets;
using PoRedoImage.Web.Configuration;

namespace PoRedoImage.Tests.Unit.Features;

/// <summary>
/// Unit tests for KeyVaultSecretNameMapping — the Adapter pattern implementation
/// that bridges Key Vault naming conventions (dashes) with .NET configuration keys (colons).
/// All secrets use the "PoRedoImage-" prefix to namespace them within the shared PoShared Key Vault.
/// </summary>
public class KeyVaultSecretNameMappingTests
{
    private readonly KeyVaultSecretNameMapping _mapping = new();

    // ─── Load tests — should accept known secrets ───────────────────

    [Theory]
    [InlineData("PoRedoImage-ComputerVision-ApiKey", true)]
    [InlineData("poredoimage-computervision-apikey", true)]
    [InlineData("POREDOIMAGE-COMPUTERVISION-APIKEY", true)]
    [InlineData("PoRedoImage-ComputerVision-Endpoint", true)]
    [InlineData("PoRedoImage-OpenAI-ApiKey", true)]
    [InlineData("PoRedoImage-OpenAI-Endpoint", true)]
    [InlineData("PoRedoImage-ApplicationInsights-ConnectionString", true)]
    [InlineData("PoRedoImage-StorageConnectionString", true)]
    [InlineData("UnknownSecret", false)]
    [InlineData("SomeOtherApp-ApiKey", false)]
    [InlineData("ComputerVision-ApiKey", false)]
    [InlineData("AzureOpenAI-ApiKey", false)]
    [InlineData("", false)]
    public void Load_EvaluatesSecretName(string secretName, bool expectedResult)
    {
        var properties = SecretModelFactory.SecretProperties(name: secretName);
        Assert.Equal(expectedResult, _mapping.Load(properties));
    }

    // ─── GetKey tests — maps secret names to config keys ────────────

    [Theory]
    [InlineData("PoRedoImage-ComputerVision-ApiKey", "ComputerVision:ApiKey")]
    [InlineData("PoRedoImage-ComputerVision-Endpoint", "ComputerVision:Endpoint")]
    [InlineData("PoRedoImage-OpenAI-ApiKey", "OpenAI:Key")]
    [InlineData("PoRedoImage-OpenAI-Endpoint", "OpenAI:Endpoint")]
    [InlineData("PoRedoImage-ApplicationInsights-ConnectionString", "ApplicationInsights:ConnectionString")]
    [InlineData("PoRedoImage-StorageConnectionString", "Storage:ConnectionString")]
    [InlineData("Foo--Bar--Baz", "Foo:Bar:Baz")]
    public void GetKey_SecretName_ReturnsMappedConfigKey(string secretName, string expectedConfigKey)
    {
        var secret = SecretModelFactory.KeyVaultSecret(
            SecretModelFactory.SecretProperties(name: secretName), "dummy-value");
        Assert.Equal(expectedConfigKey, _mapping.GetKey(secret));
    }

    [Fact]
    public void RequiredSecretNames_ContainsAllExpectedKeyVaultNames()
    {
        var expected = new[]
        {
            "PoRedoImage-ComputerVision-ApiKey",
            "PoRedoImage-ComputerVision-Endpoint",
            "PoRedoImage-OpenAI-ApiKey",
            "PoRedoImage-OpenAI-Endpoint",
            "PoRedoImage-ApplicationInsights-ConnectionString",
            "PoRedoImage-StorageConnectionString",
            "PoRedoImage-AzureAd-ClientId",
            "PoRedoImage-AzureAd-ClientSecret",
            "PoRedoImage-Google-ApiKey",
            "PoRedoImage-Google-Imagen3Model",
            // PoRedoImage-HuggingFace-ApiKey was removed in 2026-08 with the provider itself. The
            // vault secret may still exist; it is simply no longer required at startup.
        };

        Assert.Equal(expected.OrderBy(x => x), KeyVaultSecretNameMapping.RequiredSecretNames.OrderBy(x => x));
    }
}
