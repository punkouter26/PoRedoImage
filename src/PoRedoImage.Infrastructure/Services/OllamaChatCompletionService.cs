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

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "The outbound body is an anonymous type shaped to Ollama's exact contract, "
                      + "which System.Text.Json source generation cannot describe. Mirrors the "
                      + "identical suppression in OllamaVisionService; this host is never trimmed.")]
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

        var client = httpClientFactory.CreateClient("Ollama");
        using var response = await client.PostAsJsonAsync("/api/chat", payload, ct);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

        var content = doc.RootElement.TryGetProperty("message", out var message)
            && message.TryGetProperty("content", out var text)
                ? text.GetString()?.Trim() ?? string.Empty
                : string.Empty;

        // Ollama reports prompt/completion counts separately and omits them on some builds.
        var tokens =
            (doc.RootElement.TryGetProperty("prompt_eval_count", out var p) ? p.GetInt32() : 0)
            + (doc.RootElement.TryGetProperty("eval_count", out var e) ? e.GetInt32() : 0);

        var elapsed = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        logger.LogInformation(
            "Ollama chat completion finished in {Elapsed}ms. Model={Model}, Tokens={Tokens}, Image={HasImage}",
            elapsed, model, tokens, image is not null);

        return new ChatCompletionResult(content, tokens, elapsed);
    }
}
