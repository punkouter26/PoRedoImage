namespace PoRedoImage.Client.Models;

/// <summary>
/// The session's per-capability provider choices.
/// </summary>
/// <remarks>
/// Registered scoped, which in Blazor WASM lasts the lifetime of the app instance: selections
/// survive navigation between Studio and the feature pages but reset on reload. That is deliberate —
/// nothing persists to storage, so a removed provider can never leave a stale selection behind.
/// It must be a service rather than page state because Studio is where the user chooses but the
/// feature pages are where requests are issued.
/// </remarks>
public sealed class AiSelectionState
{
    private readonly Dictionary<AiCapability, string> _selections = [];

    /// <summary>Raised after any selection changes so open components can re-render.</summary>
    public event Action? OnChange;

    /// <summary>The selected provider id, falling back to the catalog default.</summary>
    public string Get(AiCapability capability) =>
        _selections.TryGetValue(capability, out var id) ? id : AiServiceCatalog.DefaultFor(capability).Id;

    /// <summary>The selected option, falling back to the catalog default when the id is unknown.</summary>
    public AiProviderOption GetOption(AiCapability capability) =>
        AiServiceCatalog.Find(capability, Get(capability)) ?? AiServiceCatalog.DefaultFor(capability);

    /// <summary>Records a selection and notifies subscribers.</summary>
    public void Set(AiCapability capability, string providerId)
    {
        if (Get(capability) == providerId) return;

        _selections[capability] = providerId;
        OnChange?.Invoke();
    }
}
