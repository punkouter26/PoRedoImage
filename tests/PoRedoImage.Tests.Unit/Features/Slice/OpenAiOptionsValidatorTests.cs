using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using PoRedoImage.Web.Configuration;

namespace PoRedoImage.Tests.Unit.Features.Slice;

/// <summary>
/// Slice tests for <see cref="OpenAiOptionsValidator"/>. Asserts:
///   - Production env: missing Endpoint/Key/Deployment → Fail (fail-fast at boot).
///   - Development env: missing fields → Success with warnings (so the app boots
///     in GUEST mode while configuration is being set up).
///   - All fields present → Success in any environment.
/// </summary>
public class OpenAiOptionsValidatorTests
{
    private static OpenAiOptionsValidator MakeValidator(bool isProduction)
    {
        var env = new HostingEnvironment { EnvironmentName = isProduction ? Environments.Production : Environments.Development };
        return new OpenAiOptionsValidator(env, NullLogger<OpenAiOptionsValidator>.Instance);
    }

    [Fact]
    public void AllFieldsPresent_Production_Succeeds()
    {
        var v = MakeValidator(isProduction: true);
        var result = v.Validate(null, new OpenAiOptions { Endpoint = "https://x.openai.azure.com/", Key = "k", ChatCompletionsDeployment = "gpt-4o" });
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void MissingEndpoint_Production_Fails()
    {
        var v = MakeValidator(isProduction: true);
        var result = v.Validate(null, new OpenAiOptions { Endpoint = "", Key = "k", ChatCompletionsDeployment = "gpt-4o" });
        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("OpenAI:Endpoint"));
    }

    [Fact]
    public void MissingKey_Production_Fails()
    {
        var v = MakeValidator(isProduction: true);
        var result = v.Validate(null, new OpenAiOptions { Endpoint = "https://x.openai.azure.com/", Key = "", ChatCompletionsDeployment = "gpt-4o" });
        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("OpenAI:Key"));
    }

    [Fact]
    public void MissingDeployment_Production_Fails()
    {
        var v = MakeValidator(isProduction: true);
        var result = v.Validate(null, new OpenAiOptions { Endpoint = "https://x.openai.azure.com/", Key = "k", ChatCompletionsDeployment = "" });
        Assert.True(result.Failed);
        Assert.Contains(result.Failures, f => f.Contains("ChatCompletionsDeployment"));
    }

    [Fact]
    public void MissingFields_Development_SucceedsWithWarnings()
    {
        var v = MakeValidator(isProduction: false);
        var result = v.Validate(null, new OpenAiOptions());
        // Development should NOT fail-fast — the app must boot so the user can configure via
        // user-secrets / Key Vault and see per-endpoint error messages.
        Assert.True(result.Succeeded);
    }
}

/// <summary>Lightweight stand-in for IWebHostEnvironment used by slice tests.</summary>
internal sealed class HostingEnvironment : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "PoRedoImage.Tests";
    public string EnvironmentName { get; set; } = Microsoft.Extensions.Hosting.Environments.Development;
    public IFileProvider ContentRootFileProvider { get; set; } = null!;
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public string WebRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider WebRootFileProvider { get; set; } = null!;
}
