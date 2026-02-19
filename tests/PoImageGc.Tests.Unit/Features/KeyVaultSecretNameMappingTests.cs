using Azure.Security.KeyVault.Secrets;
using PoImageGc.Web.Features.Diagnostics;

namespace PoImageGc.Tests.Unit.Features;

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
    [InlineData("PoRedoImage-ComputerVision-ApiKey")]
    [InlineData("PoRedoImage-ComputerVision-Endpoint")]
    [InlineData("PoRedoImage-OpenAI-ApiKey")]
    [InlineData("PoRedoImage-OpenAI-Endpoint")]
    [InlineData("PoRedoImage-OpenAI-DeploymentName")]
    [InlineData("PoRedoImage-OpenAI-ImageEndpoint")]
    [InlineData("PoRedoImage-OpenAI-ImageKey")]
    [InlineData("PoRedoImage-ApplicationInsights-ConnectionString")]
    [InlineData("PoRedoImage-StorageConnectionString")]
    public void Load_KnownSecret_ReturnsTrue(string secretName)
    {
        var properties = SecretModelFactory.SecretProperties(name: secretName);
        Assert.True(_mapping.Load(properties));
    }

    [Theory]
    [InlineData("UnknownSecret")]
    [InlineData("SomeOtherApp-ApiKey")]
    [InlineData("ComputerVision-ApiKey")]  // old unprefixed — should now be rejected
    [InlineData("AzureOpenAI-ApiKey")]     // old unprefixed — should now be rejected
    [InlineData("")]
    public void Load_UnknownSecret_ReturnsFalse(string secretName)
    {
        var properties = SecretModelFactory.SecretProperties(name: secretName);
        Assert.False(_mapping.Load(properties));
    }

    [Fact]
    public void Load_IsCaseInsensitive()
    {
        var lower = SecretModelFactory.SecretProperties(name: "poredoimage-computervision-apikey");
        var upper = SecretModelFactory.SecretProperties(name: "POREDOIMAGE-COMPUTERVISION-APIKEY");
        Assert.True(_mapping.Load(lower));
        Assert.True(_mapping.Load(upper));
    }

    // ─── GetKey tests — maps secret names to config keys ────────────

    [Theory]
    [InlineData("PoRedoImage-ComputerVision-ApiKey",             "ComputerVision:ApiKey")]
    [InlineData("PoRedoImage-ComputerVision-Endpoint",           "ComputerVision:Endpoint")]
    [InlineData("PoRedoImage-OpenAI-ApiKey",                     "OpenAI:Key")]
    [InlineData("PoRedoImage-OpenAI-Endpoint",                   "OpenAI:Endpoint")]
    [InlineData("PoRedoImage-OpenAI-DeploymentName",             "OpenAI:ChatCompletionsDeployment")]
    [InlineData("PoRedoImage-OpenAI-ImageEndpoint",              "OpenAI:ImageEndpoint")]
    [InlineData("PoRedoImage-OpenAI-ImageKey",                   "OpenAI:ImageKey")]
    [InlineData("PoRedoImage-ApplicationInsights-ConnectionString", "ApplicationInsights:ConnectionString")]
    [InlineData("PoRedoImage-StorageConnectionString",           "Storage:ConnectionString")]
    public void GetKey_KnownSecret_ReturnsMappedConfigKey(string secretName, string expectedConfigKey)
    {
        var secret = SecretModelFactory.KeyVaultSecret(
            SecretModelFactory.SecretProperties(name: secretName), "dummy-value");
        Assert.Equal(expectedConfigKey, _mapping.GetKey(secret));
    }

    [Fact]
    public void GetKey_UnknownSecret_FallsBackToDoubleHyphenReplacement()
    {
        // For secrets not in the mapping, the fallback replaces "--" with ":"
        var secret = SecretModelFactory.KeyVaultSecret(
            SecretModelFactory.SecretProperties(name: "Foo--Bar--Baz"), "val");
        Assert.Equal("Foo:Bar:Baz", _mapping.GetKey(secret));
    }
}
