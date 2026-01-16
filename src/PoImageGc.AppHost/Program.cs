var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.PoImageGc_Web>("web")
    .WithExternalHttpEndpoints();

builder.Build().Run();
