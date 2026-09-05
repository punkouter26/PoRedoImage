using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PoRedoImage.Domain.Interfaces;
using PoRedoImage.Shared.Configuration;

namespace PoRedoImage.Infrastructure.Services;

/// <summary>
/// Free-form reasoning against a local Ollama model, for the callers that were previously
/// Azure-OpenAI-or-nothing: the Style Director agents, the Rap Roast scene describer, and the roast
/// lyric writer.
/// </summary>
/// <remarks>
/// <para>
/// Ollama already served vision here (<see cref="OllamaVisionService"/>) but nothing else, so a
/// self-hosted setup still paid Azure for every piece of reasoning in the app. These are short,
/// low-stakes text jobs — a mood word, three style directions, twelve bars — and a 7B-class local
/// model handles them at zero marginal cost.
/// </para>
/// <para>
/// Selected by configuration (<c>Ollama:ChatModel</c>) rather than per request, because
/// <see cref="IChatCompletionService"/> has no router: it is a single registration, and the choice
/// is a deployment decision ("this box runs its own models") rather than a per-image one. Callers
/// see no difference — every one of them already handles <see cref="IsConfigured"/> being false and
/// falls back to a deterministic path.
/// </para>
/// <para>
/// Image content parts ARE supported: Ollama's <c>/api/chat</c> takes a base64 <c>images</c> array
/// on the message, the same shape the vision service uses. Whether the configured model can
/// actually see is the operator's problem, and the same is true of the Azure deployment.
/// </para>
/// </remarks>
public sealed class OllamaChatCompletionService(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<OllamaChatCompletionService> logger) : IChatCompletionService
{
    private string? Model => configuration[ConfigKeys.OllamaChatModel];

    /// <summary>
    /// Configured when a chat model is named. The endpoint has its own default in the named
    /// HttpClient, so naming a model is the deliberate act that turns this on.
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Model);

    private bool IsOpenAiFormat =>
        string.Equals(configuration[ConfigKeys.OllamaApiFormat], "openai", StringComparison.OrdinalIgnoreCase)
        || configuration[ConfigKeys.OllamaEndpoint]?.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) == true;

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "The outbound body is an anonymous type shaped to the local daemon's contract, "
                      + "which System.Text.Json source generation cannot describe. Mirrors OllamaVisionService.")]
    public async Task<ChatCompletionResult> CompleteAsync(
        string systemPrompt, string userPrompt, byte[]? image = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);

        var model = Model;
        if (string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException(
                "Ollama chat completion is not configured. Set Ollama:ChatModel (and Ollama:Endpoint "
                + "if the daemon is not on its default address).");

        var start = Stopwatch.GetTimestamp();
        var client = httpClientFactory.CreateClient("Ollama");

        string content;
        int tokens = 0;

        if (IsOpenAiFormat)
        {
            object userMessage = image is null
                ? new { role = "user", content = userPrompt }
                : new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = userPrompt },
                        new { type = "image_url", image_url = new { url = $"data:image/jpeg;base64,{Convert.ToBase64String(image)}" } }
                    }
                };

            var payload = new
            {
                model,
                stream = false,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    userMessage,
                },
            };

            using var response = await client.PostAsJsonAsync("/v1/chat/completions", payload, ct);
            response.EnsureSuccessStatusCode();

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            content = doc.RootElement.TryGetProperty("choices", out var choices)
                && choices.GetArrayLength() > 0
                && choices[0].TryGetProperty("message", out var msg)
                && msg.TryGetProperty("content", out var text)
                    ? text.GetString()?.Trim() ?? string.Empty
                    : string.Empty;

            if (doc.RootElement.TryGetProperty("usage", out var usage))
            {
                tokens = usage.TryGetProperty("total_tokens", out var t) ? t.GetInt32() : 0;
            }
        }
        else
        {
            object userMessage = image is null
                ? new { role = "user", content = userPrompt }
                : new { role = "user", content = userPrompt, images = new[] { Convert.ToBase64String(image) } };

            var payload = new
            {
                model,
                stream = false,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    userMessage,
                },
            };

            using var response = await client.PostAsJsonAsync("/api/chat", payload, ct);
            response.EnsureSuccessStatusCode();

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

            content = doc.RootElement.TryGetProperty("message", out var message)
                && message.TryGetProperty("content", out var text)
                    ? text.GetString()?.Trim() ?? string.Empty
                    : string.Empty;

            tokens =
                (doc.RootElement.TryGetProperty("prompt_eval_count", out var p) ? p.GetInt32() : 0)
                + (doc.RootElement.TryGetProperty("eval_count", out var e) ? e.GetInt32() : 0);
        }

        var elapsed = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        logger.LogInformation(
            "Ollama/Local chat completion finished in {Elapsed}ms. Model={Model}, Tokens={Tokens}, Image={HasImage}",
            elapsed, model, tokens, image is not null);

        return new ChatCompletionResult(content, tokens, elapsed);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Anonymous types for local streaming API payload.")]
    public async IAsyncEnumerable<string> StreamCompleteAsync(
        string systemPrompt,
        string userPrompt,
        byte[]? image = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);

        var model = Model;
        if (string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException("Ollama chat completion is not configured.");

        var client = httpClientFactory.CreateClient("Ollama");

        if (IsOpenAiFormat)
        {
            var payload = new
            {
                model,
                stream = true,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
            {
                Content = JsonContent.Create(payload)
            };
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var reader = new StreamReader(stream);

            while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
            {
                if (ct.IsCancellationRequested) break;
                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: ", StringComparison.Ordinal)) continue;
                var json = line[6..].Trim();
                if (json == "[DONE]") break;

                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var delta = choices[0].GetProperty("delta");
                    if (delta.TryGetProperty("content", out var c) && c.GetString() is { Length: > 0 } str)
                    {
                        yield return str;
                    }
                }
            }
        }
        else
        {
            object userMessage = image is null
                ? new { role = "user", content = userPrompt }
                : new { role = "user", content = userPrompt, images = new[] { Convert.ToBase64String(image) } };

            var payload = new
            {
                model,
                stream = true,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    userMessage,
                },
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
            {
                Content = JsonContent.Create(payload)
            };
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var reader = new StreamReader(stream);

            while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
            {
                if (ct.IsCancellationRequested) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("message", out var msg)
                    && msg.TryGetProperty("content", out var c)
                    && c.GetString() is { Length: > 0 } str)
                {
                    yield return str;
                }
            }
        }
    }
}
