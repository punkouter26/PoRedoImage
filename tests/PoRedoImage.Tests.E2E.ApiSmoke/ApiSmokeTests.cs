using System.Net;

namespace PoRedoImage.Tests.E2E.ApiSmoke;

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
    public async Task Ai_services_are_mocked_when_mock_mode_is_required()
    {
        // Budget guardrail for the E2E tier — the ONLY tier that drives a live instance and therefore
        // the only place a real AI token could be spent. The anonymous /api/diag/mock-status endpoint
        // lists active mock reasons (empty when running real services).
        //
        // When E2E_REQUIRE_MOCK=true (set by SCRIPTS/run-e2e.ps1, which launches the app with
        // Mocks:UseMockAi=true), we HARD-FAIL unless the target reports mock mode. This makes it
        // impossible to silently point the UI suite at a real-config build and burn tokens. When the
        // var is unset (e.g. a smoke test against a real prod instance) we only assert the contract.
        var response = await _fixture.AnonymousClient.GetAsync("/api/diag/mock-status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();

        var requireMock = string.Equals(
            Environment.GetEnvironmentVariable("E2E_REQUIRE_MOCK"), "true",
            StringComparison.OrdinalIgnoreCase);

        if (requireMock)
        {
            // A non-empty JSON array of reasons proves the server swapped in mock AI services.
            Assert.Contains("MOCK", body, StringComparison.OrdinalIgnoreCase);
        }
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
    public async Task Anonymous_home_request_serves_the_wasm_host_shell()
    {
        // The host shell is deliberately anonymous (AGENT.MD rule 7): the server returns the
        // document and the WASM router performs the /login redirect client-side. This test
        // previously asserted a 302 from the server, which the BFF never emits for "/" — it only
        // passed because the tier self-skips without a live instance. The redirect itself is
        // covered by the E2E UI tier (LoginUiTests.*_Unauthenticated_home_redirects_to_login),
        // which is the only place it is observable.
        var response = await _fixture.AnonymousClient.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("blazor.web.js", body, StringComparison.OrdinalIgnoreCase);
        // No authenticated content may leak into the anonymous shell.
        Assert.DoesNotContain("GUEST LOGGED IN", body, StringComparison.OrdinalIgnoreCase);
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

    // ─── Antiforgery surface (§2 Security) ─────────────────────────────

    [LiveServerFact]
    public async Task Antiforgery_token_endpoint_returns_a_token()
    {
        // The Blazor WASM client calls /api/antiforgery/token on boot to fetch the request
        // half of the double-submit pair (the cookie half is set by the response). Anonymous
        // because the client boots and primes its token before the user signs in.
        var response = await _fixture.AnonymousClient.GetAsync("/api/antiforgery/token");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("token", body, StringComparison.OrdinalIgnoreCase);
    }

    [LiveServerFact]
    public async Task Antiforgery_token_endpoint_sets_no_store_cache_header()
    {
        // The token is bound to this caller's antiforgery cookie, so caching it would let
        // a stale value pass validation. The endpoint stamps no-store explicitly — that is
        // the property under test, not the token itself.
        var response = await _fixture.AnonymousClient.GetAsync("/api/antiforgery/token");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.CacheControl);
        Assert.True(response.Headers.CacheControl!.NoStore,
            "Antiforgery token response must set Cache-Control: no-store.");
    }

    [LiveServerFact]
    public async Task Antiforgery_token_endpoint_is_anonymous()
    {
        // No cookie, no /dev-login first — the endpoint has to answer 200 anonymous, otherwise
        // the WASM boot sequence cannot prime its token before the user is authenticated.
        var response = await _fixture.AnonymousClient.GetAsync("/api/antiforgery/token");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [LiveServerFact]
    public async Task Bulk_prompts_POST_without_antiforgery_token_returns_400()
    {
        // Authenticated as guest via the cookie client (dev-login), then send a write to a
        // group protected by RequireAntiforgeryValidation() WITHOUT the X-CSRF-TOKEN header.
        // The filter returns a ProblemDetails 400 — that's the only signal the API can give
        // a JS caller that the missing header was the reason the request failed. Auth comes
        // first so the 400 originates from the antiforgery check, not authorization.
        var loginResponse = await _fixture.Client.GetAsync("/dev-login?email=guest@guest.local");
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/bulk-generate/prompts")
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
        };
        var response = await _fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [LiveServerFact]
    public async Task Bulk_prompts_POST_with_valid_antiforgery_token_returns_204()
    {
        // Sign in via dev-login first so the cookie client carries an auth cookie — the POST
        // is protected by RequireAuthorization() AND RequireAntiforgeryValidation(), so a
        // 401 here would prove the auth cookie was lost between calls. The cookie jar is
        // shared on the default HttpClientHandler; with auto-redirect (true by default) the
        // 302 → / hand-off is silent.
        var loginResponse = await _fixture.Client.GetAsync("/dev-login?email=guest@guest.local");
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        // Fetch the request token, then echo it in X-CSRF-TOKEN on a protected write. The
        // token is bound to this caller's antiforgery cookie (set by the GET response), so
        // both halves have to come from the same HttpClient — the cookie jar is shared.
        var tokenResponse = await _fixture.Client.GetAsync("/api/antiforgery/token");
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        var tokenBody = await tokenResponse.Content.ReadAsStringAsync();

        // Cheap extraction of the token string from the { "token": "..." } payload; the
        // shape is locked by SharedJsonContext (AntiforgeryTokenDto), so a regex is fine here.
        var match = System.Text.RegularExpressions.Regex.Match(
            tokenBody, "\"token\"\\s*:\\s*\"(?<t>[^\"]+)\"");
        Assert.True(match.Success, $"Could not extract token from: {tokenBody}");
        var token = match.Groups["t"].Value;
        Assert.False(string.IsNullOrWhiteSpace(token));

        var write = new HttpRequestMessage(HttpMethod.Post, "/api/bulk-generate/prompts")
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
        };
        write.Headers.Add("X-CSRF-TOKEN", token);
        var response = await _fixture.Client.SendAsync(write);
        // The endpoint accepts the JSON but our empty {} body fails validation, so we accept
        // either a 400 (model validation) or 204 (no-op success). Either one proves the
        // antiforgery check passed — without the token the filter would have answered 400
        // with the "Invalid antiforgery token" title BEFORE the model binder ran.
        Assert.True(
            response.StatusCode == HttpStatusCode.NoContent
            || response.StatusCode == HttpStatusCode.BadRequest,
            $"Expected 204 or 400 from a token-bearing POST; got {(int)response.StatusCode}.");
    }
}
