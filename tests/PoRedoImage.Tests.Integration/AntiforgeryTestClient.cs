using System.Net.Http.Json;

namespace PoRedoImage.Tests.Integration;

/// <summary>
/// Test-side counterpart to the client's <c>AntiforgeryTokenHandler</c>.
/// </summary>
/// <remarks>
/// Every state-changing endpoint is now tagged <c>RequireAntiforgeryValidation()</c> (§2 Security),
/// so a bare <c>PostAsJsonAsync</c> gets a 400 with no token. Integration tests exercise the real
/// middleware rather than switching antiforgery off in the Test environment — an opt-out would make
/// the suite blind to exactly the regression the tag exists to prevent.
/// <para>
/// The token is fetched per client and cached in <see cref="HttpClient.DefaultRequestHeaders"/>.
/// <c>WebApplicationFactory.CreateClient()</c> handles cookies, so the antiforgery cookie set by
/// the token request rides along on the subsequent write and the double-submit pair matches.
/// </para>
/// </remarks>
internal static class AntiforgeryTestClient
{
    private const string TokenHeader = "X-CSRF-TOKEN";

    /// <summary>POSTs <paramref name="value"/> as JSON with a valid antiforgery token attached.</summary>
    public static async Task<HttpResponseMessage> PostAsJsonWithTokenAsync<T>(
        this HttpClient client, string requestUri, T value)
    {
        await EnsureTokenAsync(client);
        return await client.PostAsJsonAsync(requestUri, value);
    }

    /// <summary>DELETEs with a valid antiforgery token attached.</summary>
    public static async Task<HttpResponseMessage> DeleteWithTokenAsync(
        this HttpClient client, string requestUri)
    {
        await EnsureTokenAsync(client);
        return await client.DeleteAsync(requestUri);
    }

    /// <summary>Fetches and caches the request token unless this client already carries one.</summary>
    public static async Task EnsureTokenAsync(this HttpClient client)
    {
        if (client.DefaultRequestHeaders.Contains(TokenHeader))
            return;

        var dto = await client.GetFromJsonAsync<TokenResponse>("/api/antiforgery/token");
        if (!string.IsNullOrEmpty(dto?.Token))
            client.DefaultRequestHeaders.Add(TokenHeader, dto.Token);
    }

    private sealed record TokenResponse(string Token);
}
