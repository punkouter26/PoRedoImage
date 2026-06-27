using Azure.Identity;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PoRedoImage.Infrastructure;
using PoRedoImage.Web.Configuration;
using PoRedoImage.Web.Features.Auth;
using PoRedoImage.Web.Features.BulkGenerate;
using PoRedoImage.Web.Features.CaptionBattle;
using PoRedoImage.Web.Features.Diagnostics;
using PoRedoImage.Web.Features.Idempotency;
using PoRedoImage.Web.Features.ImageAnalysis;
using PoRedoImage.Web.Features.MemeTemplates;
using PoRedoImage.Web.Features.StyleDirector;
using PoRedoImage.Web.Features.UserImages;
using Radzen;
using Serilog;

namespace PoRedoImage.Web;

/// <summary>
/// Extension methods for configuring services in Program.cs.
/// Single Responsibility: service registration only.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds all application services to the DI container.
    /// Follows Dependency Inversion Principle (SOLID-D).
    /// </summary>
    public static WebApplicationBuilder ConfigureServices(this WebApplicationBuilder builder)
    {
        // ─── BOMB-2: Bound request body size (Po2Logic mitigation) ──────────────────
        const int MaxRequestBodyBytes = 25 * 1024 * 1024;
        builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = MaxRequestBodyBytes);
        builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = MaxRequestBodyBytes);

        // ─── Azure Key Vault ────────────────────────────────────────────────
        ConfigureKeyVault(builder);

        // ─── Serilog ────────────────────────────────────────────────────────
        ConfigureSerilog(builder);

        // ─── OpenTelemetry → Azure Monitor ─────────────────────────────────
        ConfigureOpenTelemetry(builder);

        // ─── Core services ──────────────────────────────────────────────────
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents()
            .AddInteractiveWebAssemblyComponents()
            .AddAuthenticationStateSerialization();

        builder.Services.AddRadzenComponents();
        builder.Services.AddOpenApi();

        // ─── Strongly-typed options (Po2Logic R3) ──────────────────────────────────
        ConfigureOptions(builder);

        // ─── Idempotency (Po2Logic R5 / F6) ─────────────────────────────────────────
        // IMemoryCache backs the de-dup; IEndpointFilter applied to Write endpoints via
        // [IdempotencyRequired] marker attribute. 24h TTL prevents replays across days.
        // AddHybridCache (PoNetCaching): tiered L1+L2 cache (in-process + distributed when a
        // distributed cache is registered later) with stampede protection and tagging support.
        builder.Services.AddMemoryCache();
        builder.Services.AddHybridCache();
        builder.Services.AddScoped<IdempotencyKeyFilter>();

        // ─── Rate limiting ──────────────────────────────────────────────────
        ConfigureRateLimiting(builder);

        // ─── Health checks ──────────────────────────────────────────────────
        ConfigureHealthChecks(builder);

        // ─── HTTP client ────────────────────────────────────────────────────
        // Named "BffApi" client (used by SSR pre-render + server-side health checks) gets the
        // standard resilience pipeline (retry + circuit-breaker + timeout). Keeps transient
        // failures from propagating as 500s to the browser.
        builder.Services.AddHttpClient("BffApi")
            .AddStandardResilienceHandler(options =>
            {
                options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(4);
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
                options.Retry.MaxRetryAttempts = 3;
            });

        builder.Services.AddHttpClient();
        builder.Services.AddScoped(sp =>
        {
            var nav = sp.GetRequiredService<NavigationManager>();
            return new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(4),
                BaseAddress = new Uri(nav.BaseUri)
            };
        });

        // ─── Feature services ────────────────────────────────────────────────
        builder.Services.AddPoRedoImageInfrastructure();
        // ImageSessionService is registered in the WASM Client host (client-side concern).

        // ─── Authentication & Authorization ─────────────────────────────────
        builder.Services.AddPoRedoImageAuth(builder.Configuration, builder.Environment);

        return builder;
    }

    private static void ConfigureKeyVault(WebApplicationBuilder builder)
    {
        // ...existing code from Program.cs...
    }

    private static void ConfigureSerilog(WebApplicationBuilder builder)
    {
        // ...existing code from Program.cs...
    }

    private static void ConfigureOpenTelemetry(WebApplicationBuilder builder)
    {
        // ...existing code from Program.cs...
    }

    private static void ConfigureOptions(WebApplicationBuilder builder)
    {
        // ...existing code from Program.cs...
    }

    private static void ConfigureRateLimiting(WebApplicationBuilder builder)
    {
        // ...existing code from Program.cs...
    }

    private static void ConfigureHealthChecks(WebApplicationBuilder builder)
    {
        // ...existing code from Program.cs...
    }
}
