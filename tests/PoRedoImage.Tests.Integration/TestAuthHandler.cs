using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace PoRedoImage.Tests.Integration;

/// <summary>
/// Fake authentication handler for integration tests.
/// Automatically authenticates every request as a fixed test user —
/// no browser/cookie flow required in WebApplicationFactory.
///
/// Prod-guard: this handler must never be registered in Production. If the host
/// environment is Production, the constructor throws <see cref="InvalidOperationException"/>
/// — defence in depth so a misconfigured CI smoke test, an over-eager dev tooling
/// script, or an accidental <c>AddScheme&lt;TestAuthHandler&gt;</c> in Program.cs
/// cannot silently authenticate every request as <c>test-user-integration-001</c>.
/// </summary>
public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";
    public const string DefaultUserId = "test-user-integration-001";
    public const string UserEmail = "test@example.com";
    public const string UserName = "Test User";

    private readonly string _userId;

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration,
        IWebHostEnvironment env) : base(options, logger, encoder)
    {
        // Defensive: refuse to authenticate in Production. WebApplicationFactory<Program>
        // currently sets Development, but if a future change moves test wiring to a
        // non-Development environment by mistake, the handler will fail-fast instead of
        // silently impersonating test users in front of real traffic.
        if (env.IsProduction())
        {
            throw new InvalidOperationException(
                "TestAuthHandler must never be registered in Production. " +
                "If you see this exception, an attacker could otherwise authenticate " +
                "as 'test-user-integration-001' against a live deployment.");
        }

        _userId = configuration["TestAuth:UserId"] ?? DefaultUserId;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, _userId),
            new Claim(ClaimTypes.Name, UserName),
            new Claim(ClaimTypes.Email, UserEmail),
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
