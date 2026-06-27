using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;


namespace PoRedoImage.Tests.Integration;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// The user ID injected into TestAuthHandler via configuration.
    /// Override per-test-class to achieve storage isolation between test runs.
    /// </summary>
    public string TestUserId { get; init; } = TestAuthHandler.DefaultUserId;

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Set environment to Development to skip Key Vault configuration
        builder.UseEnvironment("Development");

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
                ["Storage:ConnectionString"] = "",
                ["Google:ApiKey"] = ""
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
        });

        return base.CreateHost(builder);
    }
}
