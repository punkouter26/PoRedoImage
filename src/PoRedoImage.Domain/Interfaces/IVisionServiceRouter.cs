namespace PoRedoImage.Domain.Interfaces;

/// <summary>
/// Selects the appropriate <see cref="IVisionService"/> backend for a requested model id.
/// Strategy pattern (GoF): keeps provider-selection logic out of the orchestrator.
/// </summary>
public interface IVisionServiceRouter
{
    /// <summary>
    /// Returns the vision service that should handle the given model id.
    /// Null or unknown ids fall back to the default (Azure) backend.
    /// </summary>
    IVisionService Resolve(string? modelId);
}
