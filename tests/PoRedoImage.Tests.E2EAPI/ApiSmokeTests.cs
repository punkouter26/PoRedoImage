using System.Net;

namespace PoRedoImage.Tests.E2EAPI;

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
        // /api/diag requires authorization; an anonymous call must be rejected (not redirected).
        var response = await _fixture.AnonymousClient.GetAsync("/api/diag");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
