using System.Net;
using System.Net.Http.Json;
using PoRedoImage.Shared.Json;

namespace PoRedoImage.Client.Http;

/// <summary>
/// Attaches the antiforgery request token to every unsafe (POST/PUT/PATCH/DELETE) BFF call.
/// </summary>
/// <remarks>
/// <para>
/// §2 requires antiforgery validation on state-changing endpoints. The secret half of the
/// double-submit pair lives in an <c>HttpOnly</c> cookie the browser attaches automatically and
/// WASM cannot read; this handler supplies the other half, fetched once from
/// <c>/api/antiforgery/token</c> and cached for the lifetime of the app instance.
/// </para>
/// <para>
/// A 400 on a write is retried exactly once with a freshly fetched token, and that retry is not
/// belt-and-braces — it is load-bearing. The request token is bound to the CALLER'S IDENTITY, so
/// the token this handler caches while the user is still anonymous stops validating the instant
/// they sign in. (Verified against the running host: replaying a token issued for one user under a
/// different identity returns 400.) Since the client boots and primes its token before login, the
/// first authenticated write in a session will normally take exactly this path. The retry is capped
/// at one attempt so a genuinely malformed request surfaces its 400 instead of looping.
/// </para>
/// </remarks>
public sealed class AntiforgeryTokenHandler : DelegatingHandler
{
    private const string TokenHeader = "X-CSRF-TOKEN";
    private const string TokenEndpoint = "api/antiforgery/token";

    private readonly Func<HttpClient> _tokenClientFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;

    /// <param name="tokenClientFactory">
    /// Supplies a client for the token fetch. Deliberately not this handler's own pipeline: the
    /// fetch is a GET that needs no token, and reentering the pipeline while holding the gate
    /// would deadlock.
    /// </param>
    public AntiforgeryTokenHandler(Func<HttpClient> tokenClientFactory)
        => _tokenClientFactory = tokenClientFactory;

    private static bool IsUnsafe(HttpMethod method) =>
        method == HttpMethod.Post || method == HttpMethod.Put ||
        method == HttpMethod.Patch || method == HttpMethod.Delete;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!IsUnsafe(request.Method))
            return await base.SendAsync(request, cancellationToken);

        await StampAsync(request, forceRefresh: false, cancellationToken);
        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode != HttpStatusCode.BadRequest)
            return response;

        // Stale token → refetch and replay once. The original response is disposed because the
        // retry supersedes it.
        var retry = await CloneAsync(request);
        if (retry is null)
            return response;

        await StampAsync(retry, forceRefresh: true, cancellationToken);
        response.Dispose();
        return await base.SendAsync(retry, cancellationToken);
    }

    private async Task StampAsync(HttpRequestMessage request, bool forceRefresh, CancellationToken ct)
    {
        var token = await GetTokenAsync(forceRefresh, ct);
        if (string.IsNullOrEmpty(token))
            return;

        request.Headers.Remove(TokenHeader);
        request.Headers.TryAddWithoutValidation(TokenHeader, token);
    }

    private async Task<string?> GetTokenAsync(bool forceRefresh, CancellationToken ct)
    {
        if (!forceRefresh && _token is { Length: > 0 })
            return _token;

        await _gate.WaitAsync(ct);
        try
        {
            // Another caller may have refreshed while we waited.
            if (!forceRefresh && _token is { Length: > 0 })
                return _token;

            using var client = _tokenClientFactory();
            var dto = await client.GetFromJsonAsync<AntiforgeryTokenResponse>(
                TokenEndpoint, SharedJsonOptions.Default, ct);
            _token = dto?.Token;
            return _token;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Offline or the endpoint is unreachable: send the request unstamped and let the
            // server decide. Failing here would turn a recoverable 400 into a client-side crash.
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Rebuilds a request for replay; returns null when the body cannot be re-read.</summary>
    private static async Task<HttpRequestMessage?> CloneAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
        };

        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        if (request.Content is not null)
        {
            // The original content stream has already been consumed by the first send, so buffer it.
            var bytes = await request.Content.ReadAsByteArrayAsync();
            var content = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            clone.Content = content;
        }

        return clone;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _gate.Dispose();
        base.Dispose(disposing);
    }

    private sealed record AntiforgeryTokenResponse(string Token);
}
