using System.Net.Http.Json;
using PoRedoImage.Shared.DTOs;
using PoRedoImage.Shared.Json;

namespace PoRedoImage.Client.Services;

/// <summary>
/// Running, session-scoped tally of estimated spend as the user interacts with different AI services,
/// computed from indicative prices returned by <c>GET /api/pricing</c>.
/// </summary>
public sealed class SessionCostService(HttpClient http, ILogger<SessionCostService> logger)
{
    private Task? _loadTask;

    /// <summary>Active provider + indicative prices, or null while unloaded / if the fetch failed.</summary>
    public AiPricingDto? Pricing { get; private set; }

    /// <summary>Images generated this session (Google Gemini / Imagen 3).</summary>
    public int ImageCount { get; private set; }

    /// <summary>Vision analysis calls this session (Azure Computer Vision / Vision).</summary>
    public int VisionCount { get; private set; }

    /// <summary>Text reasoning / prompt / lyrics completions this session (Azure OpenAI GPT).</summary>
    public int TextReasoningCount { get; private set; }

    /// <summary>Lyria music generation tracks this session (Google Lyria 3).</summary>
    public int MusicCount { get; private set; }

    /// <summary>Total count of distinct AI operations used in this session.</summary>
    public int TotalOperations => ImageCount + VisionCount + TextReasoningCount + MusicCount;

    /// <summary>Estimated spend this session across all AI services, in <see cref="AiPricingDto.Currency"/>.</summary>
    public decimal EstimatedTotal =>
        (ImageCount * (Pricing?.ImageToImageUsd ?? 0.039m)) +
        (VisionCount * (Pricing?.VisionAnalysisUsd ?? 0.001m)) +
        (TextReasoningCount * (Pricing?.TextReasoningUsd ?? 0.0015m)) +
        (MusicCount * (Pricing?.MusicGenerationUsd ?? 0.040m));

    public event Action? OnChange;

    /// <summary>
    /// Fetches pricing once, sharing one request across concurrent callers (assigned
    /// synchronously before the first await). The task never faults.
    /// </summary>
    public Task EnsureLoadedAsync() => _loadTask ??= LoadAsync();

    private async Task LoadAsync()
    {
        try
        {
            Pricing = await http.GetFromJsonAsync<AiPricingDto>("/api/pricing", SharedJsonOptions.Default);
            OnChange?.Invoke();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Session cost meter: pricing unavailable");
            _loadTask = null;
        }
    }

    /// <summary>Records <paramref name="count"/> newly generated image(s) at the image generation rate.</summary>
    public void RecordImages(int count = 1)
    {
        if (count <= 0) return;
        ImageCount += count;
        OnChange?.Invoke();
    }

    /// <summary>Records <paramref name="count"/> vision analysis call(s) (Azure Computer Vision).</summary>
    public void RecordVision(int count = 1)
    {
        if (count <= 0) return;
        VisionCount += count;
        OnChange?.Invoke();
    }

    /// <summary>Records <paramref name="count"/> text reasoning / prompt completion(s) (Azure OpenAI GPT).</summary>
    public void RecordTextReasoning(int count = 1)
    {
        if (count <= 0) return;
        TextReasoningCount += count;
        OnChange?.Invoke();
    }

    /// <summary>Records <paramref name="count"/> music generation track(s) (Google Lyria 3).</summary>
    public void RecordMusic(int count = 1)
    {
        if (count <= 0) return;
        MusicCount += count;
        OnChange?.Invoke();
    }

    public void Reset()
    {
        ImageCount = 0;
        VisionCount = 0;
        TextReasoningCount = 0;
        MusicCount = 0;
        OnChange?.Invoke();
    }

    /// <summary>Returns itemized list of non-zero AI services used this session.</summary>
    public IReadOnlyList<CostBreakdownItem> GetBreakdown()
    {
        var list = new List<CostBreakdownItem>();
        var p = Pricing;

        if (ImageCount > 0)
        {
            var unit = p?.ImageToImageUsd ?? 0.039m;
            list.Add(new("Image Generation", "bi-palette2", ImageCount, unit, ImageCount * unit));
        }

        if (VisionCount > 0)
        {
            var unit = p?.VisionAnalysisUsd ?? 0.001m;
            list.Add(new("Vision Analysis", "bi-eye", VisionCount, unit, VisionCount * unit));
        }

        if (TextReasoningCount > 0)
        {
            var unit = p?.TextReasoningUsd ?? 0.0015m;
            list.Add(new("Text & Reasoning", "bi-chat-quote", TextReasoningCount, unit, TextReasoningCount * unit));
        }

        if (MusicCount > 0)
        {
            var unit = p?.MusicGenerationUsd ?? 0.040m;
            list.Add(new("Lyria Music", "bi-music-note-beamed", MusicCount, unit, MusicCount * unit));
        }

        return list;
    }

    /// <summary>Formats an amount for display, respecting the configured currency.</summary>
    public string Format(decimal value) =>
        string.Equals(Pricing?.Currency, "USD", StringComparison.OrdinalIgnoreCase)
            ? $"${value:0.0000}"
            : $"{value:0.0000} {Pricing?.Currency}";
}

public sealed record CostBreakdownItem(
    string Name,
    string IconClass,
    int Count,
    decimal UnitPriceUsd,
    decimal SubtotalUsd);

