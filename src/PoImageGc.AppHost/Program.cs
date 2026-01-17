var builder = DistributedApplication.CreateBuilder(args);

var web = builder.AddProject<Projects.PoImageGc_Web>("web")
    .WithExternalHttpEndpoints();

// Configure production environment variables for Azure deployment
// These will be set when deployed via Aspire CLI
if (builder.ExecutionContext.IsPublishMode)
{
    web.WithEnvironment("AZURE_KEY_VAULT_ENDPOINT", "https://kv-poshared.vault.azure.net/")
       .WithEnvironment("AZURE_CLIENT_ID", "183a4196-d24f-4519-afea-783ff01dbb17")
       .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Production");
}

builder.Build().Run();
