using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;

namespace PoRedoImage.Web.Features.Auth;

public static class AuthServiceExtensions
{
    /// <summary>
    /// Authorization policy guarding the /api/diag endpoint. In Production it requires the caller's
    /// identity to be on the <c>Diagnostics:AdminEmails</c> allow-list (fail-closed: an empty list
    /// denies everyone). In non-Production any authenticated user may read diagnostics.
    /// </summary>
    public const string DiagnosticsPolicy = "DiagnosticsAccess";

    private static void AddPoRedoImageAuthorization(
        IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(DiagnosticsPolicy, policy =>
            {
                policy.RequireAuthenticatedUser();

                if (environment.IsProduction())
                {
                    var admins = (configuration["Diagnostics:AdminEmails"] ?? string.Empty)
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                    // Fail closed: with no configured admins, nobody can read prod diagnostics.
                    policy.RequireAssertion(ctx =>
                        admins.Length > 0 &&
                        ctx.User.Claims.Any(c =>
                            (c.Type is ClaimTypes.Email or ClaimTypes.Name or "email" or "preferred_username")
                            && admins.Contains(c.Value, StringComparer.OrdinalIgnoreCase)));
                }
            });
        });
    }

    /// <summary>
    /// Registers authentication and authorization for PoRedoImage.
    /// Dev (no ClientId): cookie-only. All other environments: Microsoft OIDC + cookie.
    /// </summary>
    public static IServiceCollection AddPoRedoImageAuth(
        this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        // ─── Fake-auth bypass (test/dev only) ───────────────────────────────────────
        // When Auth:EnableFakeAuth=true (and NOT Production), the entire auth pipeline is the
        // header-driven FakeAuthHandler — callers assume an identity via X-Fake-User / X-Fake-Roles.
        // This is the BFF local bypass used by headless Playwright golden-path suites.
        var enableFakeAuth = configuration.GetValue<bool>("Auth:EnableFakeAuth");
        if (enableFakeAuth && !environment.IsProduction())
        {
            services.AddAuthentication(FakeAuthHandler.SchemeName)
                .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, FakeAuthHandler>(
                    FakeAuthHandler.SchemeName, _ => { });
            AddPoRedoImageAuthorization(services, configuration, environment);
            services.AddCascadingAuthenticationState();
            return services;
        }

        var clientId = configuration["AzureAd:ClientId"];
        var hasOidc = !string.IsNullOrWhiteSpace(clientId);

        if (environment.IsDevelopment() && !hasOidc)
        {
            services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options =>
                {
                    options.LoginPath = "/login";
                    options.AccessDeniedPath = "/login";
                    options.Events.OnRedirectToLogin = ApiAwareRedirectToLogin;
                });
        }
        else
        {
            var tenantId = configuration["AzureAd:TenantId"] ?? "common";
            services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                // Cookie is also the default challenge so an unauthenticated page hit redirects to
                // /login (which offers both Microsoft OAuth and, in Dev, GUEST) rather than jumping
                // straight to Microsoft. The explicit /challenge-microsoft route invokes OIDC directly.
                options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddCookie(options =>
            {
                options.LoginPath = "/login";
                options.AccessDeniedPath = "/access-denied";
                options.Events.OnRedirectToLogin = ApiAwareRedirectToLogin;
            })
            .AddOpenIdConnect(options =>
            {
                options.Authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
                options.ClientId = clientId;
                options.ClientSecret = configuration["AzureAd:ClientSecret"];
                options.ResponseType = "code";
                options.SaveTokens = false;
                options.CallbackPath = configuration["AzureAd:CallbackPath"] ?? "/signin-oidc";
                options.SignedOutCallbackPath = configuration["AzureAd:SignedOutCallbackPath"] ?? "/signout-oidc";
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("email");
                options.GetClaimsFromUserInfoEndpoint = true;
                options.TokenValidationParameters.NameClaimType = "name";
                // API callers must get a 401, not a 302 to the Microsoft login page. Without this,
                // a WASM fetch to a protected /api route would follow the redirect and fail opaquely.
                options.Events.OnRedirectToIdentityProvider = ctx =>
                {
                    if (ctx.Request.Path.StartsWithSegments("/api"))
                    {
                        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        ctx.HandleResponse();
                    }
                    return Task.CompletedTask;
                };
                // AzureAd:AllowedTenantIds (comma-separated) restricts access to specific tenants.
                // When absent, ANY well-formed Microsoft Entra issuer is accepted — this preserves
                // the multi-tenant + personal-account behavior the user asked for ("all MS outlook
                // accounts / common authority") while rejecting malformed/unexpected issuer strings
                // that an unvalidated Authority setting could otherwise produce.
                var allowedTenants = configuration["AzureAd:AllowedTenantIds"]?
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (allowedTenants?.Length > 0)
                {
                    options.TokenValidationParameters.ValidIssuers = allowedTenants
                        .Select(t => $"https://login.microsoftonline.com/{t}/v2.0")
                        .ToArray();
                }
                else
                {
                    options.TokenValidationParameters.IssuerValidator = (issuer, _, _) =>
                        issuer is { } i
                        && i.StartsWith("https://login.microsoftonline.com/", StringComparison.OrdinalIgnoreCase)
                        && i.EndsWith("/v2.0", StringComparison.OrdinalIgnoreCase)
                            ? i
                            : throw new SecurityTokenInvalidIssuerException($"Untrusted issuer: {issuer}");
                }
            });
        }

        AddPoRedoImageAuthorization(services, configuration, environment);
        services.AddCascadingAuthenticationState();
        return services;
    }

    /// <summary>
    /// Returns 401 for /api/ requests so that API clients receive a proper
    /// unauthorized status code. Non-API paths get the normal login redirect.
    /// </summary>
    private static Task ApiAwareRedirectToLogin(
        Microsoft.AspNetCore.Authentication.RedirectContext<CookieAuthenticationOptions> ctx)
    {
        if (ctx.Request.Path.StartsWithSegments("/api"))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }
        ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    }
}
