using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading.RateLimiting;
using PoRedoImage.Infrastructure;
using PoRedoImage.Shared.Json;
using PoRedoImage.Web.Components;
using PoRedoImage.Web.Configuration;
using PoRedoImage.Web.Features.Auth;
using PoRedoImage.Web.Features.BulkGenerate;
using PoRedoImage.Web.Features.Diagnostics;
using PoRedoImage.Web.Features.Idempotency;
using PoRedoImage.Web.Features.ImageAnalysis;
using PoRedoImage.Web.Features.MemeTemplates;
using PoRedoImage.Web.Features.Pricing;
using PoRedoImage.Web.Features.RapRoast;
using PoRedoImage.Web.Features.StyleDirector;
using PoRedoImage.Web.Features.UserImages;
using Radzen;
using Serilog;
using Scalar.AspNetCore;

// ─── Bootstrap logger ───────────────────────────────────────────────
// Captures startup/Key Vault failures before the full Serilog pipeline is ready.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Warning()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ─── BOMB-2: Bound request body size (Po2Logic mitigation) ──────────────────
    // Default Kestrel limit is 30 MB. We allow 25 MB for image uploads (matches the
    // 20 MB client-side cap + JSON envelope overhead). Prevents 50 MB base64 payloads
    // from OOM-killing the App Service worker.
    const int MaxRequestBodyBytes = 25 * 1024 * 1024;
    builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = MaxRequestBodyBytes);
    builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = MaxRequestBodyBytes);

    // ─── Host bootstrap (Key Vault → Serilog → OpenTelemetry) ───────────
    // Order matters: Key Vault loads secrets FIRST so the Application Insights connection string is
    // available when Serilog and OpenTelemetry are configured. See HostBootstrapExtensions.
    builder.AddPoRedoImageKeyVault();
    builder.AddPoRedoImageTelemetry(builder.ConfigurePoRedoImageSerilog());

    // ─── Core services ──────────────────────────────────────────────────
    // Global Interactive WebAssembly (§1 BFF model): the whole UI runs in the browser; this
    // project is the API/BFF host only. AddAuthenticationStateSerialization flows the cookie-
    // authenticated principal (claims only, never tokens) to the WASM AuthenticationStateProvider.
    builder.Services.AddRazorComponents()
        .AddInteractiveWebAssemblyComponents()
        .AddAuthenticationStateSerialization();

    // Register Radzen services on the server so SSR pre-rendering can resolve
    // Radzen-injected properties on Client WASM components (e.g. NotificationService).
    builder.Services.AddRadzenComponents();

    builder.Services.AddOpenApi();

    // ─── JSON source-gen (PoNetCaching §7) ──────────────────────────────────────
    // Wire the shared DTO JsonSerializerContext into the minimal-API serializer so
    // request/response serialization is reflection-free and trim-safe. The default
    // resolver stays first in the chain so anonymous types (e.g. health-check
    // responses) and framework types fall back to reflection.
    builder.Services.ConfigureHttpJsonOptions(options =>
    {
        options.SerializerOptions.TypeInfoResolver = JsonTypeInfoResolver.Combine(
            new DefaultJsonTypeInfoResolver(),
            new SharedJsonContext());
        options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

    // ─── Strongly-typed options (Po2Logic R3) ──────────────────────────────────
    // Options-pattern binding with IValidateOptions<T> gives us hot-reloadable config
    // (Key Vault rotates every 30 min) AND startup-time validation for required fields.
    builder.Services.AddOptions<OpenAiOptions>()
        .Bind(builder.Configuration.GetSection(OpenAiOptions.SectionName))
        .ValidateOnStart();
    builder.Services.AddSingleton<IValidateOptions<OpenAiOptions>, OpenAiOptionsValidator>();

    builder.Services.AddOptions<StorageOptions>()
        .Bind(builder.Configuration.GetSection(StorageOptions.SectionName))
        .ValidateOnStart();
    builder.Services.AddSingleton<IValidateOptions<StorageOptions>, StorageOptionsValidator>();

    // Indicative AI per-image pricing surfaced to the client cost estimate (§ImageGen feature flag).
    builder.Services.Configure<AiPricingOptions>(
        builder.Configuration.GetSection(AiPricingOptions.SectionName));

    // Fail-fast secret validator (Po2Logic F7)
    builder.Services.AddHostedService<StartupSecretValidator>();

    // HTTP client factory (used by health checks)
    builder.Services.AddHttpClient();

    // ─── Caching & idempotency (Po2Logic R5 / F6 + PoNetCaching) ─────────────────
    // IMemoryCache backs the idempotency de-dup; IEndpointFilter applied to Write endpoints via
    // [IdempotencyRequired] marker attribute. 24h TTL prevents replays across days.
    // HybridCache adds a tiered L1 (+ L2 when a distributed cache is later registered) cache with
    // built-in stampede protection — consumed today by the immutable meme-template catalog.
    builder.Services.AddMemoryCache();
    builder.Services.AddHybridCache();
    builder.Services.AddScoped<IdempotencyKeyFilter>();

    // ─── Rate limiting ──────────────────────────────────────────────────
    // Protect costly AI endpoints: 10 requests/minute per authenticated user (falls back to IP).
    // Returns HTTP 429 when the limit is exceeded.
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        // Partition by user ID so one user's bulk batch cannot starve other users on the same IP
        options.AddPolicy("ai-endpoints", context =>
        {
            var userId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? context.Connection.RemoteIpAddress?.ToString()
                ?? "anonymous";
            return RateLimitPartition.GetSlidingWindowLimiter(userId, _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 2
            });
        });

        // Telemetry writes (client vitals) are cheap compared to an AI call, so they get their own
        // looser budget — but not an unlimited one: this is still an authenticated write path into
        // Table Storage. One sample per page load means a real user never approaches 30/minute.
        options.AddPolicy("telemetry", context =>
        {
            var userId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? context.Connection.RemoteIpAddress?.ToString()
                ?? "anonymous";
            return RateLimitPartition.GetFixedWindowLimiter(userId, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
        });
    });

    // ─── Health checks ──────────────────────────────────────────────────
    // Named checks verify connectivity to Computer Vision and OpenAI endpoints
    builder.Services.AddHealthChecks()
        .AddCheck<KeyVaultHealthCheck>("key-vault", tags: ["ready"])
        .AddCheck<ComputerVisionHealthCheck>("computer-vision", tags: ["ready"])
        .AddCheck<OpenAIHealthCheck>("openai", tags: ["ready"])
        .AddCheck<BulkPromptStorageHealthCheck>("table-storage", tags: ["ready"])
        .AddCheck<Imagen3HealthCheck>("imagen3", tags: ["ready"]);

    // ─── HTTP client (resilient, server-side) ───────────────────────────
    // The BFF host's own HttpClient (server-side health checks + any SSR component) points back at
    // the app's base address and runs through the standard resilience pipeline — retry + circuit-
    // breaker + timeout — so transient upstream blips don't surface to the browser as 500s.
    builder.Services.AddHttpClient("BffApi", (sp, client) =>
        {
            var nav = sp.GetRequiredService<NavigationManager>();
            client.BaseAddress = new Uri(nav.BaseUri);
            client.Timeout = TimeSpan.FromMinutes(4);
        })
        .AddStandardResilienceHandler(options =>
        {
            options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(4);
            options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(2);
            // Circuit-breaker sampling window must be >= 2x the attempt timeout.
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(4);
            options.Retry.MaxRetryAttempts = 2;
        });

    // Components inject a plain HttpClient; hand them the resilient named client.
    builder.Services.AddScoped(sp =>
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("BffApi"));

    // ─── Feature services (Onion Architecture — Infrastructure layer wires all services) ──
    // DI registration follows Dependency Inversion Principle (SOLID-D)
    builder.Services.AddPoRedoImageInfrastructure(builder.Configuration);

    // ─── Correlation on the outbound leg (§3) ──────────────────────────
    // RequestContextMiddleware handles browser → BFF. This closes the chain for BFF → downstream
    // so one correlation id spans the whole path. Infrastructure is a plain (non-ASP.NET) project
    // and cannot see IHttpContextAccessor, so the named clients it registered are re-opened here
    // by name — AddHttpClient with an existing name appends to that client's configuration.
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddTransient<OutboundCorrelationHandler>();
    foreach (var aiClient in new[] { "Ollama", "GeminiApi", "HuggingFaceApi" })
    {
        builder.Services.AddHttpClient(aiClient)
            .AddHttpMessageHandler<OutboundCorrelationHandler>();
    }

    // ImageSessionService is a client-side (WASM) concern now — registered in the Client host.

    // ─── Authentication & Authorization ─────────────────────────────────
    builder.Services.AddPoRedoImageAuth(builder.Configuration, builder.Environment);

    var app = builder.Build();

    // ─── Middleware pipeline ────────────────────────────────────────────
    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
        app.UseHsts();
    }

    // Only apply the "pretty error page" re-execute for browser (non-API) paths.
    // API requests must keep their original 4xx/5xx status codes so clients
    // don't receive a 302 redirect to the login page instead of a real 401.
    app.UseWhen(
        ctx => !ctx.Request.Path.StartsWithSegments("/api"),
        branch => branch.UseStatusCodePagesWithReExecute("/not-found"));

    // HTTPS redirect is skipped in Development so the E2E suite (default base URL
    // http://localhost:4000) gets real status codes instead of a 307 to https — the dev cert
    // already secures :4001 for interactive use. Production/Staging keep the redirect + HSTS.
    if (!app.Environment.IsDevelopment())
    {
        app.UseHttpsRedirection();
    }

    // Pushes CorrelationId, UserId, and SessionId into Serilog LogContext for every request
    app.UseMiddleware<RequestContextMiddleware>();

    // Structured request logging: one entry per request with timing and status
    app.UseSerilogRequestLogging(opts =>
    {
        opts.MessageTemplate =
            "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
        opts.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value ?? string.Empty);
            diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
            diagnosticContext.Set("CorrelationId",
                httpContext.Response.Headers["X-Correlation-ID"].FirstOrDefault() ?? string.Empty);
        };
    });

    app.UseRateLimiter();
    app.UseAntiforgery();
    app.UseAuthentication();

    // The Blazor WebAssembly boot assets under /_framework must load for anonymous users: the whole
    // UI — including the /login page — is Interactive WebAssembly and cannot boot without them. Most
    // of these are static files served by MapStaticAssets().AllowAnonymous(), BUT the runtime-generated
    // boot manifest (resource-collection.js) is NOT a static asset, so it is not covered by that opt-out.
    // The fail-closed FallbackPolicy (RequireAuthenticatedUser) then 302-redirects it to /login, and the
    // browser — following the redirect and receiving the login HTML instead of the JS module — fails the
    // subresource-integrity check and never boots the app (blank page). Routing has already matched the
    // endpoint by this point, so we tag any /_framework endpoint that lacks an explicit authorization
    // opt-out with AllowAnonymous before UseAuthorization evaluates the fallback policy. Every real
    // endpoint (no /_framework prefix) stays fail-closed.
    app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/_framework")
            && context.GetEndpoint() is { } endpoint
            && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null)
        {
            var metadata = new List<object>(endpoint.Metadata) { new AllowAnonymousAttribute() };
            context.SetEndpoint(new Endpoint(
                endpoint.RequestDelegate,
                new EndpointMetadataCollection(metadata),
                endpoint.DisplayName));
        }

        await next();
    });

    app.UseAuthorization();

    // OpenAPI + Scalar API documentation (public — the FallbackPolicy would otherwise gate them)
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();

    // Health check endpoints
    app.MapHealthChecks("/health", new HealthCheckOptions
    {
        ResponseWriter = async (context, report) =>
        {
            context.Response.ContentType = "application/json";
            // JsonStringEnumConverter ensures per-entry Status is serialized as
            // "Healthy"/"Degraded"/"Unhealthy" (not a raw int) so the post-deploy
            // smoke test can grep the failing check name without parsing numbers.
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                Converters = { new JsonStringEnumConverter() },
                WriteIndented = false
            };
            await context.Response.WriteAsJsonAsync(new
            {
                Status = report.Status.ToString(),
                Duration = report.TotalDuration.TotalMilliseconds,
                Entries = report.Entries.Select(e => new
                {
                    e.Key,
                    Status = e.Value.Status.ToString(),
                    Duration = e.Value.Duration.TotalMilliseconds,
                    Description = e.Value.Description,
                    Error = e.Value.Exception?.Message
                })
            }, options);
        }
    }).AllowAnonymous();
    app.MapHealthChecks("/alive", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();

    // Minimal API endpoints (Vertical Slice)
    app.MapAuthEndpoints();
    app.MapImageAnalysisEndpoints();
    app.MapDiagnosticsEndpoints();
    app.MapVitalsEndpoints();
    app.MapBulkGenerateEndpoints();
    app.MapUserImageEndpoints();
    app.MapMemeTemplateEndpoints();
    app.MapStyleDirectorEndpoints();
    app.MapRapRoastEndpoints();
    app.MapPricingEndpoints();

    // Redirect /favicon.ico → /favicon.png so browsers don't get a 404.
    app.MapGet("/favicon.ico", () => Results.Redirect("/favicon.png", permanent: true))
        .ExcludeFromDescription()
        .AllowAnonymous();

    // Static assets (WASM runtime, css/js) and the SPA host shell must load for anonymous users so the
    // client-side login page can render; server-side data endpoints stay protected via RequireAuthorization.
    app.MapStaticAssets().AllowAnonymous();
    app.MapRazorComponents<App>()
        .AddInteractiveWebAssemblyRenderMode()
        .AddAdditionalAssemblies(typeof(PoRedoImage.Client._Imports).Assembly)
        .AllowAnonymous();

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "PoRedoImage terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// Make Program class accessible to integration tests
public partial class Program { }
