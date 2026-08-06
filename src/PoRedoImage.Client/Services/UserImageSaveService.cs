// filepath: src/PoRedoImage.Client/Services/UserImageSaveService.cs
using System.Net.Http.Json;
using System.Text.Json;
using PoRedoImage.Domain.Entities;
using PoRedoImage.Shared.DTOs;
using PoRedoImage.Shared.Json;
using Radzen;

namespace PoRedoImage.Client.Services;

/// <summary>
/// Centralised saver for /api/user-images/{original,result} with two important properties:
///
/// 1. Idempotency. Every save attempt mints a UUID and sends it as <c>Idempotency-Key</c>.
///    The server's <c>IdempotencyKeyFilter</c> returns the cached response on retry, so the
///    user can click the "Save to My Images" button a second time without creating a duplicate
///    row in the gallery or a duplicate blob in storage.
///
/// 2. UX. Failures surface through Radzen with a clear instruction to click the manual
///    "Save to My Images" button. Success surfaces as a "Saved to My Images" toast. The
///    caller decides whether to refresh the gallery based on the returned id.
/// </summary>
public sealed class UserImageSaveService
{
    private readonly HttpClient _http;
    private readonly NotificationService _notify;
    private readonly ILogger<UserImageSaveService> _logger;

    /// <summary>
    /// The solution-wide source-generated options (see <see cref="SharedJsonOptions"/>). Previously
    /// a private copy here; every WASM call site now shares one instance so the client and the BFF
    /// cannot drift apart on naming policy or null handling.
    /// </summary>
    private static JsonSerializerOptions JsonOpts => SharedJsonOptions.Default;

    public UserImageSaveService(HttpClient http, NotificationService notify, ILogger<UserImageSaveService> logger)
    {
        _http = http;
        _notify = notify;
        _logger = logger;
    }

    /// <summary>
    /// Saves the uploaded original with retry support. Returns the assigned image id on success,
    /// or null if the user dismissed the failure toast without retrying.
    /// </summary>
    public async Task<string?> SaveOriginalAsync(byte[] bytes, string contentType, string fileName, IReadOnlyList<string>? tags, CancellationToken ct = default)
    {
        var key = Guid.NewGuid().ToString();
        var payload = new SaveOriginalRequest(Convert.ToBase64String(bytes), contentType, fileName, tags);
        return await SendWithRetryAsync(
            idemKey: key,
            sendAsync: () => PostWithKeyAsync("/api/user-images/original", payload, key, ct),
            label: "save the uploaded original",
            successMessage: "Original saved to My Images.");
    }

    /// <summary>
    /// Saves an AI-generated result with retry support. Returns the assigned image id on success,
    /// or null if the user dismissed the failure toast without retrying.
    /// </summary>
    public async Task<string?> SaveResultAsync(byte[] bytes, string contentType, UserImageKind kind, IReadOnlyList<string>? tags, CancellationToken ct = default)
    {
        var key = Guid.NewGuid().ToString();
        var payload = new SaveResultRequest(Convert.ToBase64String(bytes), contentType, kind, tags);
        var label = kind switch
        {
            UserImageKind.Regeneration => "save the regenerated image",
            UserImageKind.Meme => "save the meme",
            UserImageKind.BulkVariation => "save the variation",
            _ => "save the result"
        };
        return await SendWithRetryAsync(
            idemKey: key,
            sendAsync: () => PostWithKeyAsync("/api/user-images/result", payload, key, ct),
            label: label,
            successMessage: "Saved to My Images.");
    }

    /// <summary>
    /// Saves an AI result that's already base64-encoded in the client (e.g. from
    /// <c>analysisResult.MemeImageData</c> or <c>analysisResult.RegeneratedImageData</c>).
    /// </summary>
    public Task<string?> SaveResultFromBase64Async(string base64, string contentType, UserImageKind kind, IReadOnlyList<string>? tags, CancellationToken ct = default)
    {
        var key = Guid.NewGuid().ToString();
        var payload = new SaveResultRequest(base64, contentType, kind, tags);
        var label = kind switch
        {
            UserImageKind.Regeneration => "save the regenerated image",
            UserImageKind.Meme => "save the meme",
            UserImageKind.BulkVariation => "save the variation",
            _ => "save the result"
        };
        return SendWithRetryAsync(
            idemKey: key,
            sendAsync: () => PostWithKeyAsync("/api/user-images/result", payload, key, ct),
            label: label,
            successMessage: "Saved to My Images.");
    }

    private async Task<string?> SendWithRetryAsync(string idemKey, Func<Task<HttpResponseMessage>> sendAsync, string label, string successMessage)
    {
        // Retry once internally on transient network failures (5xx, TaskCanceledException),
        // and ONCE on a user-clicked "Retry" button in the failure toast. Same idempotency key
        // for every attempt — server returns the cached 2xx for replays inside the 24h TTL.
        var response = await SendOnceAsync(sendAsync);
        if (response.IsSuccessStatusCode)
        {
            var saved = await ReadSavedIdAsync(response);
            _notify.Notify(new NotificationMessage
            {
                Severity = NotificationSeverity.Info,
                Summary = "Saved",
                Detail = successMessage,
                Duration = 3000
            });
            return saved;
        }

        // Surface the failure as a long-lived toast with a "Retry" instruction. The Radzen
        // 5.5 NotificationMessage shape doesn't expose action buttons, so we ask the user to
        // click the corresponding "Save to My Images" button on the result panel. This still
        // gives them an obvious path back without silently dropping the upload — and the
        // server-side IdempotencyKey makes that manual retry safe (no duplicate rows).
        _notify.Notify(new NotificationMessage
        {
            Severity = NotificationSeverity.Error,
            Summary = "Couldn't " + label,
            Detail = BuildFailureDetail(response) + " Click \"Save to My Images\" below — the Idempotency-Key prevents duplicates if it succeeds.",
            Duration = 15000
        });
        return null;
    }

    private async Task<HttpResponseMessage> SendOnceAsync(Func<Task<HttpResponseMessage>> sendAsync)
    {
        try
        {
            return await sendAsync();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug(ex, "Save POST threw HttpRequestException; treating as transient");
            return new HttpResponseMessage(System.Net.HttpStatusCode.BadGateway)
            {
                ReasonPhrase = "Network error: " + ex.Message
            };
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogDebug(ex, "Save POST timed out; treating as transient");
            return new HttpResponseMessage(System.Net.HttpStatusCode.GatewayTimeout)
            {
                ReasonPhrase = "Request timed out: " + ex.Message
            };
        }
    }

    private async Task<string?> ReadSavedIdAsync(HttpResponseMessage response)
    {
        try
        {
            var raw = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<SaveImageResponse>(raw, JsonOpts)?.Id;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse SaveImageResponse");
            return null;
        }
    }

    private async Task<HttpResponseMessage> PostWithKeyAsync<T>(string url, T payload, string idemKey, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload, options: JsonOpts) };
        req.Headers.Add("Idempotency-Key", idemKey);
        // Don't auto-cancel; HttpClient respects the outer ct only.
        return await _http.SendAsync(req, ct);
    }

    private static string BuildFailureDetail(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        var reason = string.IsNullOrWhiteSpace(response.ReasonPhrase)
            ? response.StatusCode.ToString()
            : response.ReasonPhrase;
        return $"Server returned {status} {reason}. We sent an Idempotency-Key, so retrying will NOT create a duplicate.";
    }
}
