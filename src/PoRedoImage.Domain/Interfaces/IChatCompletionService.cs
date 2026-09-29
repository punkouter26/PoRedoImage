namespace PoRedoImage.Domain.Interfaces;

/// <summary>
/// General-purpose chat-completion abstraction: the scene describer, reproduction prompt and lyric writer.
/// Unlike <see cref="IGenerativeAiService"/> (task-specific: enhance description, meme caption,
/// describe person), this is a free-form reasoning primitive: give it a system + user prompt and,
/// optionally, an image, and it returns the model's text. Kept separate so these callers can be
/// backed by Azure OpenAI or Ollama without touching the task-specific service surface.
/// </summary>
public interface IChatCompletionService
{
    /// <summary>
    /// <c>true</c> when a provider + credentials are configured. When <c>false</c>, callers must
    /// fall back to their own deterministic behaviour rather than invoking <see cref="CompleteAsync"/>.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Runs a single chat completion. When <paramref name="image"/> is supplied, a vision-capable
    /// model is used and the image travels as a data-URI content part. When
    /// <paramref name="jsonSchema"/> is supplied, the reply is constrained to that JSON schema
    /// (strict structured output) instead of trusting the prompt to ask nicely.
    /// </summary>
    Task<ChatCompletionResult> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        byte[]? image = null,
        string? jsonSchema = null,
        CancellationToken ct = default);
}

/// <summary>Result of a chat completion — the model's text plus usage/timing telemetry.</summary>
public sealed record ChatCompletionResult(string Content, int TokensUsed, long ElapsedMs);
