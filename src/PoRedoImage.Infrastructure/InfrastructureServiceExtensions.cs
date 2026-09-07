using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Http.Resilience;
using PoRedoImage.Application.Configuration;
using PoRedoImage.Application.Features.BulkGenerate;
using PoRedoImage.Application.Features.ImageAnalysis;
using PoRedoImage.Application.Features.RapRoast;
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
    /// Optional. The Infrastructure layer reads <c>Mocks:UseMockAi</c> from this for the
    /// defence-in-depth checks inside individual AI service classes (which throw if a real
    /// service is constructed while mocks are enabled — see <c>MockAiDelegatingHandler</c>).
    /// </param>
    /// <param name="useMockAi">
    /// Whether to register the mock AI services instead of the real ones. This MUST be
    /// resolved by the host using <c>MockAiGate.IsEnabled(...)</c> so the Test-only policy
    /// is enforced in exactly one place. Passing <c>false</c> wires the real services and
    /// is the safe default for Dev and Prod.
    /// </param>
    public static IServiceCollection AddPoRedoImageInfrastructure(
        this IServiceCollection services,
        IConfiguration? configuration = null,
        bool useMockAi = false)
    {

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
            services.AddSingleton<IImageGenerationRouter>(sp =>
                new SingleImageGenerationRouter(sp.GetRequiredService<IImageGenerationService>()));

            // Chat completion (Rap Roast scene describer + lyric writer): the mock reports
            // IsConfigured=false so callers take their deterministic path — zero network, stable output.
            services.AddSingleton<MockChatCompletionService>();
            services.AddSingleton<IChatCompletionService>(sp => sp.GetRequiredService<MockChatCompletionService>());
            services.AddSingleton<IMockable>(sp => sp.GetRequiredService<MockChatCompletionService>());

            // OCR / region captions are a real network call, so mock mode gets the null provider.
            services.AddSingleton<ISceneDetailProvider, NullSceneDetailProvider>();

            services.AddSingleton<MockLyriaMusicService>();
            services.AddSingleton<IMusicGenerationService>(sp => sp.GetRequiredService<MockLyriaMusicService>());
            services.AddSingleton<IMockable>(sp => sp.GetRequiredService<MockLyriaMusicService>());

            services.AddSingleton<MockVeoVideoGenerationService>();
            services.AddSingleton<IVideoGenerationService>(sp => sp.GetRequiredService<MockVeoVideoGenerationService>());
            services.AddSingleton<IMockable>(sp => sp.GetRequiredService<MockVeoVideoGenerationService>());
        }
        else
        {
            // Vision backends: Azure Computer Vision (default/cloud) + Ollama (local image-to-text) + Gemini Vision.
            // The router picks per-request based on the selected model id.
            services.AddSingleton<AzureVisionService>();
            services.AddSingleton<OllamaVisionService>();
            services.AddSingleton<OpenAiVisionService>();
            services.AddSingleton<GeminiVisionService>();

            // Vision is memoised by image content hash. The decorator wraps the DEFAULT service
            // only — the router hands out the concrete backends, so Ollama and the OpenAI vision
            // path get their own wrappers below rather than sharing one and colliding on a key
            // that says nothing about which model produced the answer.
            services.AddSingleton<IVisionService>(sp => new CachingVisionService(
                sp.GetRequiredService<AzureVisionService>(),
                sp.GetRequiredService<IMemoryCache>(),
                sp.GetRequiredService<ILogger<CachingVisionService>>(),
                "vision:azure-cv"));

            services.AddSingleton<IVisionServiceRouter>(sp => new VisionServiceRouter(
                sp.GetRequiredService<AzureVisionService>(),
                sp.GetRequiredService<OllamaVisionService>(),
                sp.GetRequiredService<OpenAiVisionService>(),
                sp.GetRequiredService<IMemoryCache>(),
                sp.GetRequiredService<ILoggerFactory>(),
                sp.GetRequiredService<GeminiVisionService>()));

            services.AddSingleton<AzureOpenAiService>();
            services.AddSingleton<IGenerativeAiService>(sp => new CachingGenerativeAiService(
                sp.GetRequiredService<AzureOpenAiService>(),
                sp.GetRequiredService<IMemoryCache>(),
                sp.GetRequiredService<ILogger<CachingGenerativeAiService>>()));

            // Image generation: Google Gemini/Imagen, with optional fast/budget tier.
            services.AddSingleton<GeminiImagen3Service>();
            services.AddSingleton<IImageGenerationService>(sp =>
                sp.GetRequiredService<GeminiImagen3Service>());

            services.AddSingleton<IImageGenerationRouter>(sp =>
            {
                var standard = sp.GetRequiredService<GeminiImagen3Service>();
                var config = sp.GetRequiredService<IConfiguration>();
                var fastModel = config[ConfigKeys.GoogleImagen3FastModel];
                var fast = !string.IsNullOrWhiteSpace(fastModel)
                    ? new GeminiImagen3Service(
                        config,
                        sp.GetRequiredService<IHttpClientFactory>(),
                        sp.GetRequiredService<ILogger<GeminiImagen3Service>>(),
                        fastModel)
                    : null;
                return new ImageGenerationRouter(standard, fast);
            });

            // Chat + vision powering the Rap Roast scene describer and its lyric writer.
            // Azure OpenAI is the only backend: one deployment serves both text
            // and image content parts, so image-to-text needs no second provider or model id.
            //
            // Note for anyone re-adding a provider switch here: every caller of
            // IChatCompletionService catches failures and silently falls back to heuristics or a
            // tag-derived description. That makes a broken backend invisible in the UI and expensive
            // to diagnose — which is exactly how the HuggingFace outage above went unnoticed. A new
            // backend needs its failures surfaced, not swallowed.
            // Ollama takes over reasoning when a local chat model is named, so a self-hosted
            // deployment stops paying Azure for a mood word and three style directions. Resolved
            // once at startup: this is a deployment decision, not a per-request one.
            if (!string.IsNullOrWhiteSpace(configuration?[ConfigKeys.OllamaChatModel]))
            {
                services.AddSingleton<IChatCompletionService, OllamaChatCompletionService>();
            }
            else
            {
                services.AddSingleton<IChatCompletionService, AzureOpenAiChatCompletionService>();
            }

            // Image-to-video for the Video slice: Google Veo 3.1 Lite at 720p. The most expensive
            // call in the app per invocation ($0.40 per 8-second clip), which is why the Lite tier
            // is the default and the mock above is wired for every non-production run.
            services.AddSingleton<IVideoGenerationService, VeoVideoGenerationService>();

            // Music generation for the Rap Roast slice: Google Lyria, which performs supplied
            // lyrics rather than producing an instrumental bed.
            services.AddSingleton<IMusicGenerationService, LyriaMusicService>();

            // OCR (Read), region captions (DenseCaptions), objects and people — the grounded facts
            // the scene describer hands to the vision model so it does not have to guess them.
            services.AddSingleton<ISceneDetailProvider, AzureSceneDetailService>();
        }

        // Scoped services
        services.AddScoped<IMemeGeneratorService, ImageSharpMemeGeneratorService>();
        services.AddSingleton<IMemeTemplateService, MemeTemplateService>();

        // Repository: Singleton — TableClient is thread-safe; avoids redundant CreateIfNotExists calls per-request
        services.AddSingleton<IBulkPromptRepository, AzureTableBulkPromptRepository>();

        // User image gallery: Singleton — BlobContainerClient + TableClient are both thread-safe
        services.AddSingleton<IUserImageRepository, AzureBlobUserImageRepository>();
        services.AddScoped<IUserImageService, UserImageService>();

        // Application layer orchestrator + the vision pass that writes its image-generation prompt
        // (Transient so the scoped logger flows correctly, matching SceneDescriber below).
        services.AddTransient<ReproductionPromptWriter>();
        services.AddScoped<IImageAnalysisOrchestrator, ImageAnalysisOrchestrator>();

        // Bulk board fan-out (concurrency cap, per-slot failure policy, re-roll seeding)
        services.AddScoped<IBulkGenerationService, BulkGenerationService>();

        // Rap Roast slice: lyric writer + orchestrator (Transient so scoped logger flows correctly,
        // matching the Style Director agent registrations below).
        services.AddTransient<SceneDescriber>();
        services.AddTransient<RoastLyricsWriter>();
        services.AddScoped<IRapRoastOrchestrator, RapRoastOrchestrator>();

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

        // Named HttpClient for Gemini/Lyria with standard resilience: retry, timeout, circuit-breaker
        services.AddHttpClient("GeminiApi")
            .AddHttpMessageHandler<MockAiDelegatingHandler>()
            .AddStandardResilienceHandler(ConfigureGenerativeAiResilience);

        // Named HttpClient for Veo video (long-running). No resilience pipeline: Veo renders
        // take 1–5 minutes, well past the AttemptTimeout / TotalRequestTimeout the standard
        // resilience handler would otherwise impose. A single long-poll call is exactly the
        // shape the BFF promises to the client.
        services.AddHttpClient("Veo", c =>
        {
            c.BaseAddress = new Uri(BaseUrl);
            c.Timeout = TimeSpan.FromMinutes(15);
        })
        .AddHttpMessageHandler<MockAiDelegatingHandler>();

        return services;
    }

    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/";

    /// <summary>
    /// Resilience for the generative-AI HTTP client (Gemini image generation and Lyria music).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>AttemptTimeout must be set explicitly.</b> <c>AddStandardResilienceHandler</c> defaults it
    /// to <b>10 seconds</b>, and that default is per-attempt, so raising only
    /// <see cref="HttpStandardResilienceOptions.TotalRequestTimeout"/> to five minutes did nothing:
    /// every call still had ten seconds to finish. Image generation and Lyria music generation
    /// routinely take 30–90s, so each attempt died at 10s, burned both retries, and surfaced as
    /// <c>TimeoutRejectedException</c> — which the Rap Roast endpoint reported to the user as
    /// "Something went wrong making your track." It broke image regeneration, bulk generate, and
    /// style director the same way; only small/fast calls ever completed.
    /// </para>
    /// <para>
    /// The three values are interdependent and the handler validates them at startup:
    /// <c>TotalRequestTimeout >= AttemptTimeout</c>, and
    /// <c>CircuitBreaker.SamplingDuration >= 2 × AttemptTimeout</c>. Changing one means checking
    /// the others, or the app throws on boot. Total is the real ceiling: three attempts at two
    /// minutes would be six, so the five-minute total is what actually cuts a run off.
    /// </para>
    /// </remarks>
    private static void ConfigureGenerativeAiResilience(HttpStandardResilienceOptions options)
    {
        options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(2);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(5);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(4);
        options.Retry.MaxRetryAttempts = 2;
    }
}
