namespace PoRedoImage.Tests.E2EAPI;

/// <summary>
/// Shared HTTP clients for the E2E API suite. <see cref="Client"/> follows redirects and
/// carries cookies (so the GUEST dev-bypass session at <c>/dev-login</c> sticks);
/// <see cref="AnonymousClient"/> stays unauthenticated for negative auth assertions.
/// </summary>
public sealed class E2EApiFixture : IDisposable
{
    public string BaseUrl { get; } = ResolveBaseUrl();
    public HttpClient Client { get; }
    public HttpClient AnonymousClient { get; }

    public E2EApiFixture()
    {
        var baseAddress = new Uri(BaseUrl);
        Client = new HttpClient { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(30) };
        AnonymousClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = baseAddress,
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    public static string ResolveBaseUrl() =>
        Environment.GetEnvironmentVariable("E2E_BASE_URL") ?? "http://localhost:5000";

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
