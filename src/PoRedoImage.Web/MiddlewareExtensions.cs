using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using PoRedoImage.Web.Features.Diagnostics;
using Scalar.AspNetCore;
using Serilog;

namespace PoRedoImage.Web;

/// <summary>
/// Extension methods for configuring the middleware pipeline in Program.cs.
/// Single Responsibility: pipeline configuration only (SRP).
/// </summary>
public static class MiddlewareExtensions
{
    /// <summary>
    /// Configures the middleware pipeline for the application.
    /// Order matters: authentication → authorization → endpoints.
    /// </summary>
    public static WebApplication ConfigureMiddleware(this WebApplication app)
    {
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
        app.UseHttpsRedirection();

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
        app.UseAuthorization();

        // OpenAPI + Scalar API documentation
        app.MapOpenApi();
        app.MapScalarApiReference();

        // Health check endpoints
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = async (context, report) =>
            {
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new
                {
                    Status = report.Status.ToString(),
                    Duration = report.TotalDuration.TotalMilliseconds,
                    Entries = report.Entries.Select(e => new
                    {
                        e.Key,
                        e.Value.Status,
                        e.Value.Duration,
                        e.Value.Description
                    })
                });
            }
        });

        return app;
    }
}
