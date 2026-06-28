using System.Net;

namespace PoRedoImage.Tests.E2E;

/// <summary>
/// End-to-end API tests: drive the public HTTP surface of a running instance exactly
/// as a real client would. Marked <see cref="LiveServerFactAttribute"/> so they self-skip
/// when no instance is reachable, and run for real against one that is.
/// </summary>
public sealed class ApiSmokeTests : IClassFixture<E2EApiFixture>
{
    private readonly E2EApiFixture _fixture;

    public ApiSmokeTests(E2EApiFixture fixture) => _fixture = fixture;

    [LiveServerFact]
    public async Task Health_returns_200_and_reports_status()
    {
        var response = await _fixture.Client.GetAsync("/health");

        // Healthy / Degraded entries both yield 200. Unhealthy entries would
        // surface a 503; in dev, missing optional Azure secrets should be
        // reported as Degraded so /health stays green for uptime monitoring.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Status", body, StringComparison.OrdinalIgnoreCase);
    }

    [LiveServerFact]
    public async Task Alive_liveness_probe_returns_200()
    {
        var response = await _fixture.Client.GetAsync("/alive");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [LiveServerFact]
    public async Task Protected_api_rejects_anonymous_with_401()
    {
        var response = await _fixture.AnonymousClient.GetAsync("/api/diag");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [LiveServerFact]
    public async Task Favicon_ico_redirects_to_png()
    {
        // Browsers auto-request /favicon.ico; the server returns 301 (permanent)
        // pointing at /favicon.png so the 404 noise disappears from logs.
        // AnonymousClient is used so an auto-redirect doesn't swallow the 301.
        var response = await _fixture.AnonymousClient.GetAsync("/favicon.ico");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.EndsWith("/favicon.png", response.Headers.Location!.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [LiveServerFact]
    public async Task Favicon_png_returns_image()
    {
        var response = await _fixture.Client.GetAsync("/favicon.png");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Content.Headers.ContentType);
        Assert.StartsWith("image/", response.Content.Headers.ContentType!.MediaType);
    }

    [LiveServerFact]
    public async Task Home_page_renders_for_authenticated_user()
    {
        var response = await _fixture.Client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        // The home page is a Blazor WASM bootstrap shell — verifies the
        // AuthenticationState cookie is honored end-to-end.
        Assert.Contains("<title>", body, StringComparison.OrdinalIgnoreCase);
    }

    [LiveServerFact]
    public async Task Bulk_generate_page_renders_when_authenticated()
    {
        var response = await _fixture.Client.GetAsync("/bulk-generate");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [LiveServerFact]
    public async Task Anonymous_home_request_redirects_to_login()
    {
        // Unauthenticated users must NOT see the studio — the BFF should
        // redirect to /login.
        var response = await _fixture.AnonymousClient.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var loc = response.Headers.Location!.ToString();
        Assert.Contains("/login", loc, StringComparison.OrdinalIgnoreCase);
    }

    [LiveServerFact]
    public async Task Dev_login_endpoint_issues_cookie()
    {
        var response = await _fixture.AnonymousClient.GetAsync(
            "/dev-login?email=guest@guest.local");

        // /dev-login is a 302 → final landing page is 200 after follow.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
    }
}
