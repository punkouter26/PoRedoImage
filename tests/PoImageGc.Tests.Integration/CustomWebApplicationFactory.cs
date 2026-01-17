using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace PoImageGc.Tests.Integration;

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
            // Override services for testing if needed
        });

        return base.CreateHost(builder);
    }
}
