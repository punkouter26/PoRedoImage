using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using PoRedoImage.Application.Agents;
using PoRedoImage.Application.Agents.StyleDirector;
using PoRedoImage.Application.Features.ImageAnalysis;
using PoRedoImage.Application.Features.UserImages;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Infrastructure.Repositories;
using PoRedoImage.Infrastructure.Services;
using PoRedoImage.Infrastructure.Services.Mocks;

namespace PoRedoImage.Infrastructure;

/// <summary>
/// Dependency injection registration for all Infrastructure and Application services.
/// Follows the Extension Method pattern — clean registration entry point for the server host.
/// Dependency Inversion Principle (SOLID-D): server project depends on abstractions, not implementations.
/// </summary>
public static class InfrastructureServiceExtensions
{
    /// <param name="configuration">
    /// Optional. When <c>Mocks:UseMockAi</c> is <c>true</c>, the three high-cost AI services
    /// (Vision, OpenAI text, Imagen3) are replaced with zero-network mock implementations that
    /// also implement <see cref="IMockable"/> — driving the client "USING MOCK DATA" banner and
    /// guaranteeing zero live token spend. Passing <c>null</c> always wires the real services.
    /// </param>
    public static IServiceCollection AddPoRedoImageInfrastructure(
        this IServiceCollection services, IConfiguration? configuration = null)
    {
        var useMockAi = configuration?.GetValue<bool>("Mocks:UseMockAi") ?? false;

        // Domain service implementations (Singleton: clients own long-lived HTTP/SDK resources)
        if (useMockAi)
        {
            // Register the concrete mock once and surface it under BOTH its service interface and
            // IMockable, so the banner can enumerate reasons without constructing the service twice.
            services.AddSingleton<MockVisionService>();
            services.AddSingleton<IVisionService>(sp => sp.GetRequiredService<MockVisionService>());
            services.AddSingleton<IMockable>(sp => sp.GetRequiredService<MockVisionService>());

            services.AddSingleton<MockGenerativeAiService>();
            services.AddSingleton<IGenerativeAiService>(sp => sp.GetRequiredService<MockGenerativeAiService>());
            services.AddSingleton<IMockable>(sp => sp.GetRequiredService<MockGenerativeAiService>());

            services.AddSingleton<MockImagen3Service>();
            services.AddSingleton<IImagen3Service>(sp => sp.GetRequiredService<MockImagen3Service>());
            services.AddSingleton<IMockable>(sp => sp.GetRequiredService<MockImagen3Service>());
        }
        else
        {
            services.AddSingleton<IVisionService, AzureVisionService>();
            services.AddSingleton<IGenerativeAiService, AzureOpenAiService>();
            services.AddSingleton<IImagen3Service, GeminiImagen3Service>();
        }

        // Scoped services
        services.AddScoped<IMemeGeneratorService, ImageSharpMemeGeneratorService>();
        services.AddSingleton<IMemeTemplateService, MemeTemplateService>();

        // Repository: Singleton — TableClient is thread-safe; avoids redundant CreateIfNotExists calls per-request
        services.AddSingleton<IBulkPromptRepository, AzureTableBulkPromptRepository>();

        // User image gallery: Singleton — BlobContainerClient + TableClient are both thread-safe
        services.AddSingleton<IUserImageRepository, AzureBlobUserImageRepository>();
        services.AddScoped<IUserImageService, UserImageService>();

        // Application layer orchestrator
        services.AddScoped<IImageAnalysisOrchestrator, ImageAnalysisOrchestrator>();

        // Idea #5 — Meme Caption Battle: persona-fanned-out caption generation.
        services.AddSingleton<ICaptionBattleService, CaptionBattleService>();

        // Idea #1 — Agentic Style Director: 4-agent sequential workflow.
        // Registered as transient so per-request scoped DI services (logger) flow correctly.
        services.AddTransient<VisionAnalystAgent>();
        services.AddTransient<StyleStrategistAgent>();
        services.AddTransient<PromptRefinerAgent>();
        services.AddTransient<CriticAgent>();
        services.AddTransient<StyleDirectorWorkflow>();

        // Named HttpClient for Gemini with standard resilience: retry, timeout, circuit-breaker
        services.AddHttpClient("GeminiApi")
            .AddStandardResilienceHandler(options =>
            {
                options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(5);
                options.Retry.MaxRetryAttempts = 2;
            });

        return services;
    }
}
