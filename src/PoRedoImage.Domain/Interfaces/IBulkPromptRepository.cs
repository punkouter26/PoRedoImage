using PoRedoImage.Domain.Entities;

namespace PoRedoImage.Domain.Interfaces;

/// <summary>
/// Repository abstraction for BulkPrompt storage.
/// Dependency Inversion Principle (SOLID-D): higher layers depend on this abstraction,
/// not on the concrete Azure Table Storage implementation.
/// </summary>
public interface IBulkPromptRepository
{
    Task<BulkPrompt?> GetByRowKeyAsync(string rowKey, CancellationToken ct = default);
    Task SaveAsync(BulkPrompt prompt, CancellationToken ct = default);
    Task DeleteAsync(string rowKey, CancellationToken ct = default);
}
