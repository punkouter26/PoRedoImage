using Microsoft.Extensions.DependencyInjection;
using PoRedoImage.Application.Services;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Infrastructure.Repositories;
using PoRedoImage.Infrastructure.Services;

namespace PoRedoImage.Infrastructure;

/// <summary>
/// Dependency injection registration for all Infrastructure and Application services.
/// Follows the Extension Method pattern — clean registration entry point for the server host.
/// Dependency Inversion Principle (SOLID-D): server project depends on abstractions, not implementations.
/// </summary>
public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddPoRedoImageInfrastructure(this IServiceCollection services)
    {
        // Domain service implementations (Singleton: clients own long-lived HTTP/SDK resources)
        services.AddSingleton<IVisionService, AzureVisionService>();
        services.AddSingleton<IGenerativeAiService, AzureOpenAiService>();
        services.AddSingleton<IImagen3Service, GeminiImagen3Service>();

        // Scoped services
        services.AddScoped<IMemeGeneratorService, ImageSharpMemeGeneratorService>();

        // Repository (Scoped: matches request lifetime)
        services.AddScoped<IBulkPromptRepository, AzureTableBulkPromptRepository>();

        // Application layer orchestrator
        services.AddScoped<IImageAnalysisOrchestrator, ImageAnalysisOrchestrator>();

        return services;
    }
}
