namespace PoRedoImage.Tests.E2E.ApiSmoke;

/// <summary>
/// Shared HTTP clients for the API smoke tests. <see cref="Client"/> follows redirects and
/// carries cookies (so the GUEST dev-bypass session at <c>/dev-login</c> sticks);
/// <see cref="AnonymousClient"/> stays unauthenticated for negative auth assertions.
/// </summary>
public sealed class E2EApiFixture : IDisposable
{
    public string BaseUrl => LiveServerFactAttribute.BaseUrl;
    public HttpClient Client { get; }
    public HttpClient AnonymousClient { get; }

    public E2EApiFixture()
    {
        var baseAddress = new Uri(BaseUrl);
        Client = new HttpClient { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(30) };
        // UseCookies = false is critical: this client is shared across the class fixture, and a
        // sibling test (Dev_login_endpoint_issues_cookie) hits /dev-login which returns Set-Cookie.
        // With the default cookie container the auth cookie would stick to this "anonymous" client,
        // making later negative-auth tests (e.g. /api/diag → 401) flakily pass as 200 depending on
        // xUnit's execution order. Disabling the cookie container keeps it permanently anonymous.
        AnonymousClient = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
        })
        {
            BaseAddress = baseAddress,
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    /// <summary>True when the instance answers a /health probe within the timeout.</summary>
    public async Task<bool> IsReachableAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var response = await Client.GetAsync("/health", cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        Client.Dispose();
        AnonymousClient.Dispose();
    }
}
