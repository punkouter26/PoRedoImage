using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using PoRedoImage.Web.Configuration;

namespace PoRedoImage.Tests.Integration.Contracts;

/// <summary>
/// Contract tests for <see cref="OpenAiOptionsValidator"/>. Asserts the dev-policy contract:
///   - <c>Mocks:UseMockAi=true</c>: validator is short-circuited (real services aren't wired).
///   - <c>Mocks:UseMockAi=false</c> + missing fields: Fail in EVERY environment (Production AND
///     Development). The old warn-and-continue-in-Development behavior masked missing-key setups
///     and only surfaced as a 401 on the first AI call.
///   - All fields present + Mocks off: Success in any environment.
///
/// Lives in the Integration tier (not Unit) because it pins the options-binding CONTRACT
/// that gates host startup — see the "Contractual Integration Testing" audit item.
/// </summary>
public class OpenAiOptionsValidatorTests
{
    private static OpenAiOptionsValidator MakeValidator(
        bool isProduction,
        bool useMockAi = false)
    {
        var env = new HostingEnvironment
        {
            EnvironmentName = isProduction
                ? Microsoft.Extensions.Hosting.Environments.Production
                : Microsoft.Extensions.Hosting.Environments.Development
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mocks:UseMockAi"] = useMockAi ? "true" : "false"
            })
            .Build();
        return new OpenAiOptionsValidator(env, config, NullLogger<OpenAiOptionsValidator>.Instance);
    }

    [Fact]
    public void AllFieldsPresent_Production_Succeeds()
    {
        var v = MakeValidator(isProduction: true);
        var result = v.Validate(null, new OpenAiOptions { Endpoint = "https://x.openai.azure.com/", Key = "k", ChatCompletionsDeployment = "gpt-4o" });
        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("endpoint", "OpenAI:Endpoint")]
    [InlineData("key", "OpenAI:Key")]
    [InlineData("deployment", "ChatCompletionsDeployment")]
    public void MissingField_Production_Fails(string blankedField, string expectedFailureFragment)
    {
        var v = MakeValidator(isProduction: true);
        var options = new OpenAiOptions
        {
            Endpoint = blankedField == "endpoint" ? "" : "https://x.openai.azure.com/",
            Key = blankedField == "key" ? "" : "k",
            ChatCompletionsDeployment = blankedField == "deployment" ? "" : "gpt-4o",
        };

        var result = v.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains(expectedFailureFragment));
    }

    // ── New dev-policy contract: real AI in dev means real keys; no silent degradation. ──

    [Fact]
    public void MissingFields_Development_MocksOff_Fails()
    {
        // Dev previously passed with warnings; the new contract is that Dev also fails fast
        // when real services are wired (Mocks:UseMockAi=false). Otherwise the first AI call
        // returns 401 and the user has no clear signal that the keys are missing.
        var v = MakeValidator(isProduction: false, useMockAi: false);
        var result = v.Validate(null, new OpenAiOptions());
        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("OpenAI:Endpoint"));
        Assert.Contains(result.Failures, f => f.Contains("OpenAI:Key"));
    }

    [Fact]
    public void MissingKey_Development_MocksOff_Fails()
    {
        var v = MakeValidator(isProduction: false, useMockAi: false);
        var result = v.Validate(null, new OpenAiOptions
        {
            Endpoint = "https://x.openai.azure.com/",
            Key = "",
            ChatCompletionsDeployment = "gpt-4o"
        });
        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("OpenAI:Key"));
    }

    [Fact]
    public void MissingFields_Development_MocksOn_Succeeds()
    {
        // Explicit offline-mode opt-out: real services aren't wired, so the bound options are
        // never consumed and the validator must NOT block startup.
        var v = MakeValidator(isProduction: false, useMockAi: true);
        var result = v.Validate(null, new OpenAiOptions());
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void AllFieldsPresent_Development_MocksOff_Succeeds()
    {
        var v = MakeValidator(isProduction: false, useMockAi: false);
        var result = v.Validate(null, new OpenAiOptions
        {
            Endpoint = "https://x.openai.azure.com/",
            Key = "k",
            ChatCompletionsDeployment = "gpt-4o"
        });
        Assert.True(result.Succeeded);
    }
}

/// <summary>Lightweight stand-in for IWebHostEnvironment used by contract tests.</summary>
internal sealed class HostingEnvironment : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "PoRedoImage.Tests";
    public string EnvironmentName { get; set; } = Microsoft.Extensions.Hosting.Environments.Development;
    public IFileProvider ContentRootFileProvider { get; set; } = null!;
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public string WebRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider WebRootFileProvider { get; set; } = null!;
}
