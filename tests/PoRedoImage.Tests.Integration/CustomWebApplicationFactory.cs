using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Infrastructure.Services.Mocks;
using PoRedoImage.Web.Configuration;


namespace PoRedoImage.Tests.Integration;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// The user ID injected into TestAuthHandler via configuration.
    /// Override per-test-class to achieve storage isolation between test runs.
    /// </summary>
    public string TestUserId { get; init; } = TestAuthHandler.DefaultUserId;

    /// <summary>
    /// Storage connection string injected into the host. Empty by default (storage features become
    /// graceful no-ops). <see cref="AzuriteWebApplicationFactory"/> overrides this with a live
    /// Testcontainers Azurite endpoint so storage-backed endpoints can be exercised end-to-end.
    /// </summary>
    protected virtual string StorageConnectionString => "";

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Set environment to Test so /auth/login/fake, the FakeAuthHandler registration path,
        // and any future "is this a test run?" branches fire correctly (not "Development" — the
        // spec defines Test as the integration/E2E tier's env name; Development is for the local
        // F5 inner loop only). AddPoRedoImageKeyVault also skips when AZURE_KEY_VAULT_ENDPOINT
        // is empty, which we set in the ConfigureAppConfiguration block below, so KV load is
        // disabled in this env regardless of the env name.
        builder.UseEnvironment(PoEnvironments.Test);

        // ConfigureAppConfiguration runs AFTER the host builder loads appsettings.json +
        // appsettings.{Environment}.json, so the in-memory overrides win on conflict. Earlier
        // versions used ConfigureHostConfiguration (which runs first) — appsettings.json then
        // overwrote our Storage:ConnectionString = "" with "UseDevelopmentStorage=true", causing
        // EnsureInitializedAsync to try to connect to Azurite and 500.
        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AZURE_KEY_VAULT_ENDPOINT"] = "",
                ["TestAuth:UserId"] = TestUserId,
                ["ComputerVision:Endpoint"] = "https://test.cognitiveservices.azure.com/",
                ["ComputerVision:ApiKey"] = "test-key",
                ["OpenAI:Endpoint"] = "https://test.openai.azure.com/",
                ["OpenAI:Key"] = "test-key",
                ["ApplicationInsights:ConnectionString"] = "",
                ["Storage:ConnectionString"] = StorageConnectionString,
                ["Google:ApiKey"] = "",
                // Budget guardrail: force the mock AI services so NO test can spend a live token
                // against Azure OpenAI / Computer Vision / Google Gemini. The placeholder keys above
                // would otherwise let a service instantiate and attempt a real (failing) call.
                ["Mocks:UseMockAi"] = "true"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Override authentication: test handler always authenticates as TestAuthHandler.UserId.
            // PostConfigure runs after Program.cs, so this correctly overrides dev cookie auth.
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultScheme = TestAuthHandler.SchemeName;
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                options.DefaultForbidScheme = TestAuthHandler.SchemeName;
                options.DefaultSignInScheme = TestAuthHandler.SchemeName;
                options.DefaultSignOutScheme = TestAuthHandler.SchemeName;
            });

            // Budget guardrail: swap the real AI clients for zero-network mocks. Done here (not via
            // the Mocks:UseMockAi flag) because Program.cs reads that flag during the builder phase,
            // before the factory's in-memory config is applied — so a ConfigureServices override is
            // the only reliable way to guarantee no integration test can spend a live token.
            services.RemoveAll<IVisionService>();
            services.RemoveAll<IGenerativeAiService>();
            services.RemoveAll<IImageGenerationService>();

            services.AddSingleton<MockVisionService>();
            services.AddSingleton<IVisionService>(sp => sp.GetRequiredService<MockVisionService>());
            services.AddSingleton<IMockable>(sp => sp.GetRequiredService<MockVisionService>());

            services.AddSingleton<MockGenerativeAiService>();
            services.AddSingleton<IGenerativeAiService>(sp => sp.GetRequiredService<MockGenerativeAiService>());
            services.AddSingleton<IMockable>(sp => sp.GetRequiredService<MockGenerativeAiService>());

            services.AddSingleton<MockImagen3Service>();
            services.AddSingleton<IImageGenerationService>(sp => sp.GetRequiredService<MockImagen3Service>());
            services.AddSingleton<IMockable>(sp => sp.GetRequiredService<MockImagen3Service>());
        });

        return base.CreateHost(builder);
    }
}
