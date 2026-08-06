using System.Net.Http.Json;
using PoRedoImage.Shared.DTOs;
using PoRedoImage.Shared.Json;

namespace PoRedoImage.Client.Services;

/// <summary>
/// Running, session-scoped tally of what the images generated so far cost, using the indicative
/// per-image prices from <c>GET /api/pricing</c>.
/// </summary>
/// <remarks>
/// Worth surfacing persistently rather than only on Bulk Generate: generation is billed per image
/// (Gemini ≈ $0.039), so a ten-slot bulk run is a real spend the user should see accumulating rather
/// than discover later. These are indicative prices from config — they are not billing data and
/// never claim to be.
/// </remarks>
public sealed class SessionCostService(HttpClient http, ILogger<SessionCostService> logger)
{
    private Task? _loadTask;

    /// <summary>Active provider + per-image prices, or null while unloaded / if the fetch failed.</summary>
    public AiPricingDto? Pricing { get; private set; }

    /// <summary>Images generated this session.</summary>
    public int ImageCount { get; private set; }

    /// <summary>Estimated spend this session, in <see cref="AiPricingDto.Currency"/>.</summary>
    public decimal EstimatedTotal { get; private set; }

    public event Action? OnChange;

    /// <summary>
    /// Fetches pricing once, sharing one request across concurrent callers (assigned
    /// synchronously before the first await). The task never faults.
    /// </summary>
    /// <remarks>
    /// A failed attempt does NOT stick: <c>/api/pricing</c> requires auth, and the footer chip
    /// calls this on first render — which for a fresh session is the anonymous login page. Caching
    /// that 401 would leave the meter permanently blank for the rest of the visit.
    /// </remarks>
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
            // Non-critical: with no pricing the chip simply stays hidden.
            logger.LogDebug(ex, "Session cost meter: pricing unavailable");
            _loadTask = null; // allow a retry once the caller is authenticated
        }
    }

    /// <summary>Records <paramref name="count"/> newly generated image(s) at the image-to-image rate.</summary>
    public void RecordImages(int count = 1)
    {
        if (count <= 0) return;
        ImageCount += count;
        EstimatedTotal += (Pricing?.ImageToImageUsd ?? 0m) * count;
        OnChange?.Invoke();
    }

    public void Reset()
    {
        ImageCount = 0;
        EstimatedTotal = 0m;
        OnChange?.Invoke();
    }

    /// <summary>Formats an amount for display, respecting the configured currency.</summary>
    public string Format(decimal value) =>
        string.Equals(Pricing?.Currency, "USD", StringComparison.OrdinalIgnoreCase)
            ? $"${value:0.####}"
            : $"{value:0.####} {Pricing?.Currency}";
}
