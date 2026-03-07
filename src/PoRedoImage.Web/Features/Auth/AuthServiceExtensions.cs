using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace PoRedoImage.Web.Features.Auth;

public static class AuthServiceExtensions
{
    /// <summary>
    /// Registers authentication and authorization for PoRedoImage.
    /// Dev (no ClientId): cookie-only. All other environments: Microsoft OIDC + cookie.
    /// </summary>
    public static IServiceCollection AddPoRedoImageAuth(
        this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        var clientId = configuration["AzureAd:ClientId"];
        var hasOidc = !string.IsNullOrWhiteSpace(clientId);

        if (environment.IsDevelopment() && !hasOidc)
        {
            services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options =>
                {
                    options.LoginPath = "/login";
                    options.AccessDeniedPath = "/login";
                });
        }
        else
        {
            var tenantId = configuration["AzureAd:TenantId"] ?? "common";
            services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie(options =>
            {
                options.LoginPath = "/login";
                options.AccessDeniedPath = "/access-denied";
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
                // The 'common' endpoint returns per-user tenant IDs in the issuer claim.
                // Disable strict issuer validation for personal + multi-tenant accounts.
                options.TokenValidationParameters.ValidateIssuer = false;
            });
        }

        services.AddAuthorization();
        services.AddCascadingAuthenticationState();
        return services;
    }
}
