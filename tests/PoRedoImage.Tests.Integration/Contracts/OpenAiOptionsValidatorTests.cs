using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using PoRedoImage.Web.Configuration;

namespace PoRedoImage.Tests.Integration.Contracts;

/// <summary>
/// Contract tests for <see cref="OpenAiOptionsValidator"/>. Asserts the policy contract:
///   - <c>Mocks:UseMockAi=true</c> in the Test env: validator is short-circuited (real services
///     aren't wired; mocks take their place). The Test env is the ONLY one where the flag is
///     honoured — see MockAiGate.
///   - <c>Mocks:UseMockAi=true</c> in Development or Production: the gate ignores the flag, so
///     real services are wired AND the validator runs as if the flag were off. Setting the flag
///     in Dev no longer silently degrades to canned output.
///   - <c>Mocks:UseMockAi=false</c> + missing fields: Fail in EVERY environment (Production AND
///     Development). The old warn-and-continue-in-Development behavior masked missing-key setups
///     and only surfaced as a 401 on the first AI call.
///   - All fields present + Mocks off: Success in any environment.
///
/// Lives in the Integration tier (not Unit) because it pins the options-binding CONTRACT
/// that gates host startup. One theory, one row per rule — these were eight methods of the same
/// shape, eight of the tier's fifty.
/// </summary>
public class OpenAiOptionsValidatorTests
{
    private const string Prod = "Production", Dev = "Development", Test = PoEnvironments.Test;

    /// <param name="blank">Which field to leave empty: none, endpoint, key, deployment, or all.</param>
    /// <param name="expectedFailure">A fragment every failing case must report; null when it succeeds.</param>
    [Theory]
    [InlineData(Prod, false, "none", null)]
    [InlineData(Prod, false, "endpoint", "OpenAI:Endpoint")]
    [InlineData(Prod, false, "key", "OpenAI:Key")]
    [InlineData(Prod, false, "deployment", "ChatCompletionsDeployment")]
    [InlineData(Dev, false, "none", null)]
    // Dev also fails fast when real services are wired; otherwise the first AI call 401s.
    [InlineData(Dev, false, "all", "OpenAI:Endpoint")]
    [InlineData(Dev, false, "key", "OpenAI:Key")]
    // Mock mode is Test-only: the flag in Development must NOT short-circuit the validator.
    [InlineData(Dev, true, "all", "OpenAI:Endpoint")]
    // The Test env honours the flag — real services aren't wired, so missing keys are fine...
    [InlineData(Test, true, "all", null)]
    // ...but only when the flag is on.
    [InlineData(Test, false, "all", "OpenAI:Endpoint")]
    public void Validator_enforces_the_key_policy_per_environment(
        string env, bool useMockAi, string blank, string? expectedFailure)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Mocks:UseMockAi"] = useMockAi ? "true" : "false" })
            .Build();
        var validator = new OpenAiOptionsValidator(
            new HostingEnvironment { EnvironmentName = env }, config, NullLogger<OpenAiOptionsValidator>.Instance);

        var result = validator.Validate(null, new OpenAiOptions
        {
            Endpoint = blank is "endpoint" or "all" ? "" : "https://x.openai.azure.com/",
            Key = blank is "key" or "all" ? "" : "k",
            ChatCompletionsDeployment = blank is "deployment" or "all" ? "" : "gpt-5.4-nano",
        });

        if (expectedFailure is null)
        {
            Assert.True(result.Succeeded);
        }
        else
        {
            Assert.True(result.Failed);
            Assert.Contains(result.Failures, f => f.Contains(expectedFailure));
        }
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
