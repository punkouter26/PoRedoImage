using PoRedoImage.Domain.Entities;

namespace PoRedoImage.Domain.Interfaces;

/// <summary>
/// Persistence abstraction for browser-measured performance samples.
/// Dependency Inversion Principle (SOLID-D): the Diagnostics slice depends on this port,
/// not on Azure Table Storage.
/// </summary>
public interface IClientVitalsRepository
{
    /// <summary>Appends one sample. Never throws when storage is unconfigured — see the adapter.</summary>
    Task SaveAsync(ClientVitalsSample sample, CancellationToken ct = default);

    /// <summary>
    /// Returns up to <paramref name="max"/> samples from the last <paramref name="days"/> UTC days,
    /// newest first. Empty when storage is unconfigured.
    /// </summary>
    Task<IReadOnlyList<ClientVitalsSample>> GetRecentAsync(int days, int max, CancellationToken ct = default);
}
