using System.Text.Json;
using Microsoft.JSInterop;

namespace PoRedoImage.Client.Services;

/// <summary>
/// A localStorage-backed list of art-style prompts the user has actually used, plus the subset
/// they pinned. Lets the prompt drawer refill a slot with something that worked instead of
/// retyping it.
/// </summary>
/// <remarks>
/// Deliberately client-only: this is a convenience cache, not user data worth a table-storage
/// round trip — <c>/api/bulk-generate/prompts</c> already exists for the durable, cross-device
/// copy. Failures (private browsing, quota) degrade to an empty list rather than surfacing.
/// </remarks>
public sealed class PromptHistoryService(IJSRuntime js, ILogger<PromptHistoryService> logger)
{
    /// <summary>Cap on remembered prompts. Beyond this the oldest unpinned entry is dropped.</summary>
    private const int MaxEntries = 20;

    private List<PromptEntry>? _entries;

    /// <summary>Raised after the list changes so open drawers re-render.</summary>
    public event Action? OnChange;

    public sealed record PromptEntry(string Text, bool Pinned);

    /// <summary>Pinned first, then most-recently-used. Empty until <see cref="LoadAsync"/> runs.</summary>
    public IReadOnlyList<PromptEntry> Entries =>
        _entries is null ? [] : [.. _entries.OrderByDescending(e => e.Pinned)];

    public async Task LoadAsync()
    {
        if (_entries is not null) return;
        _entries = [];
        try
        {
            var json = await js.InvokeAsync<string?>("poUx.loadPromptHistory");
            if (string.IsNullOrEmpty(json)) return;
            var stored = JsonSerializer.Deserialize<List<PromptEntry>>(json);
            if (stored is not null) _entries = [.. stored.Take(MaxEntries)];
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Prompt history unavailable — starting empty");
        }
        OnChange?.Invoke();
    }

    /// <summary>
    /// Records <paramref name="text"/> as most-recently-used. A prompt already in the list moves
    /// to the front and keeps its pinned flag rather than being duplicated.
    /// </summary>
    public Task RememberAsync(string? text)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return Task.CompletedTask;

        _entries ??= [];
        var existing = _entries.FirstOrDefault(e => e.Text == trimmed);
        _entries.RemoveAll(e => e.Text == trimmed);
        _entries.Insert(0, existing ?? new PromptEntry(trimmed, false));
        Trim();
        return PersistAsync();
    }

    public Task TogglePinAsync(string text)
    {
        if (_entries is null) return Task.CompletedTask;
        var idx = _entries.FindIndex(e => e.Text == text);
        if (idx < 0) return Task.CompletedTask;
        _entries[idx] = _entries[idx] with { Pinned = !_entries[idx].Pinned };
        return PersistAsync();
    }

    public Task RemoveAsync(string text)
    {
        if (_entries is null) return Task.CompletedTask;
        _entries.RemoveAll(e => e.Text == text);
        return PersistAsync();
    }

    /// <summary>Drops the oldest unpinned entries once the list exceeds <see cref="MaxEntries"/>.</summary>
    private void Trim()
    {
        if (_entries is null || _entries.Count <= MaxEntries) return;
        for (var i = _entries.Count - 1; i >= 0 && _entries.Count > MaxEntries; i--)
        {
            if (!_entries[i].Pinned) _entries.RemoveAt(i);
        }
    }

    private async Task PersistAsync()
    {
        OnChange?.Invoke();
        try { await js.InvokeVoidAsync("poUx.savePromptHistory", JsonSerializer.Serialize(_entries)); }
        catch (Exception ex) { logger.LogDebug(ex, "Could not persist prompt history"); }
    }
}
