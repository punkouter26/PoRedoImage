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
using PoRedoImage.Shared.Configuration;

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
        var useMockAi = configuration?.GetValue<bool>(ConfigKeys.MocksUseMockAi) ?? false;

        // Domain service implementations (Singleton: clients own long-lived HTTP/SDK resources)
        if (useMockAi)
        {
            // Register the concrete mock once and surface it under BOTH its service interface and
            // IMockable, so the banner can enumerate reasons without constructing the service twice.
            services.AddSingleton<MockVisionService>();
            services.AddSingleton<IVisionService>(sp => sp.GetRequiredService<MockVisionService>());
            services.AddSingleton<IMockable>(sp => sp.GetRequiredService<MockVisionService>());
            services.AddSingleton<IVisionServiceRouter>(sp =>
                new SingleVisionServiceRouter(sp.GetRequiredService<MockVisionService>()));

            services.AddSingleton<MockGenerativeAiService>();
            services.AddSingleton<IGenerativeAiService>(sp => sp.GetRequiredService<MockGenerativeAiService>());
            services.AddSingleton<IMockable>(sp => sp.GetRequiredService<MockGenerativeAiService>());

            services.AddSingleton<MockImagen3Service>();
            services.AddSingleton<IImageGenerationService>(sp => sp.GetRequiredService<MockImagen3Service>());
            services.AddSingleton<IMockable>(sp => sp.GetRequiredService<MockImagen3Service>());

            // Chat completion (Style Director reasoning): mock reports IsConfigured=false so the agents
            // deterministically use their heuristic path — zero network, stable test output.
            services.AddSingleton<MockChatCompletionService>();
            services.AddSingleton<IChatCompletionService>(sp => sp.GetRequiredService<MockChatCompletionService>());
            services.AddSingleton<IMockable>(sp => sp.GetRequiredService<MockChatCompletionService>());
        }
        else
        {
            // Vision backends: Azure Computer Vision (default/cloud) + Ollama (local image-to-text).
            // The router picks per-request based on the selected model id.
            services.AddSingleton<AzureVisionService>();
            services.AddSingleton<OllamaVisionService>();
            services.AddSingleton<IVisionService>(sp => sp.GetRequiredService<AzureVisionService>());
            services.AddSingleton<IVisionServiceRouter, VisionServiceRouter>();

            services.AddSingleton<IGenerativeAiService, AzureOpenAiService>();

            // Image generation provider is switchable via the ImageGen:Provider feature flag:
            //   "google"      → Gemini/Imagen (default)
            //   "huggingface" → FLUX via HuggingFace Inference Providers (cheaper — see backlog).
            // Both concretes register so a config flip needs no redeploy; the flag picks the active one.
            services.AddSingleton<GeminiImagen3Service>();
            services.AddSingleton<HuggingFaceImageGenerationService>();
            var imageProvider = (configuration?[ConfigKeys.ImageGenProvider] ?? "google").Trim().ToLowerInvariant();
            services.AddSingleton<IImageGenerationService>(sp => imageProvider switch
            {
                "huggingface" or "hf" => sp.GetRequiredService<HuggingFaceImageGenerationService>(),
                _ => sp.GetRequiredService<GeminiImagen3Service>()
            });

            // Chat completion powering the Style Director reasoning agents. HuggingFace Inference
            // Providers (OpenAI-compatible chat + a vision model) is the real-AI backend; when its
            // token is absent the agents fall back to their heuristics (IsConfigured guards this).
            services.AddSingleton<IChatCompletionService, HuggingFaceChatCompletionService>();
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

        // Idea #1 — Agentic Style Director: 4-agent sequential workflow.
        // Registered as transient so per-request scoped DI services (logger) flow correctly.
        services.AddTransient<VisionAnalystAgent>();
        services.AddTransient<StyleStrategistAgent>();
        services.AddTransient<PromptRefinerAgent>();
        services.AddTransient<CriticAgent>();
        services.AddTransient<StyleDirectorWorkflow>();

        // Defense-in-depth budget guardrail: an HTTP-pipeline interceptor that blocks any outbound AI
        // call when Mocks:UseMockAi=true. Registered on the AI named clients below. In mock mode the
        // real clients aren't even resolved (services are swapped above), so this only ever fires on a
        // future regression — at which point it fails loud instead of spending a live token.
        services.AddTransient<MockAiDelegatingHandler>();

        // Named HttpClient for local Ollama (image-to-text via gemma4 etc.).
        // Long timeout: first call may load the model into memory; no retries (local, fail fast).
        var ollamaEndpoint = configuration?[ConfigKeys.OllamaEndpoint] ?? "http://localhost:11434";
        services.AddHttpClient("Ollama", c =>
        {
            c.BaseAddress = new Uri(ollamaEndpoint);
            c.Timeout = TimeSpan.FromMinutes(5);
        })
        .AddHttpMessageHandler<MockAiDelegatingHandler>();

        // Named HttpClient for Gemini with standard resilience: retry, timeout, circuit-breaker
        services.AddHttpClient("GeminiApi")
            .AddHttpMessageHandler<MockAiDelegatingHandler>()
            .AddStandardResilienceHandler(options =>
            {
                options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(5);
                options.Retry.MaxRetryAttempts = 2;
            });

        // Named HttpClient for HuggingFace Inference Providers — same resilience + mock guardrail.
        services.AddHttpClient("HuggingFaceApi")
            .AddHttpMessageHandler<MockAiDelegatingHandler>()
            .AddStandardResilienceHandler(options =>
            {
                options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(5);
                options.Retry.MaxRetryAttempts = 2;
            });

        return services;
    }
}
