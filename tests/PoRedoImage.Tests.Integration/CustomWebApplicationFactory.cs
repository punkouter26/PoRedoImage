using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace PoRedoImage.Tests.Integration;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Set environment to Development to skip Key Vault configuration
        builder.UseEnvironment("Development");

        builder.ConfigureHostConfiguration(config =>
        {
            // Clear Key Vault endpoint to prevent Azure authentication attempts in CI
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AZURE_KEY_VAULT_ENDPOINT"] = "",
                ["ComputerVision:Endpoint"] = "https://test.cognitiveservices.azure.com/",
                ["ComputerVision:ApiKey"] = "test-key",
                ["OpenAI:Endpoint"] = "https://test.openai.azure.com/",
                ["OpenAI:Key"] = "test-key",
                ["ApplicationInsights:ConnectionString"] = ""
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
