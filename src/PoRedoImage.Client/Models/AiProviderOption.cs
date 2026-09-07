namespace PoRedoImage.Client.Models;

/// <summary>
/// One selectable provider for a capability.
/// </summary>
/// <param name="Id">Namespaced id from <c>AiProviderIds</c>; sent to the BFF.</param>
/// <param name="DisplayName">Label shown in the dropdown.</param>
/// <param name="Category">Optgroup heading — "Remote", "Web Browser", or "Ollama".</param>
/// <param name="Hint">Short qualifier shown after the name, e.g. download size or cost.</param>
/// <param name="ExecutesInBrowser">
/// When true the client runs this model itself and posts the result instead of asking the server.
/// </param>
/// <param name="DevOnly">
/// When true the option is only offered while the client is running in Development. Ollama is the
/// case this exists for: it needs a service listening on the developer's own machine, so offering
/// it to a deployed visitor advertises a choice that can only fail for them. Filtered at render
/// time by <c>AiServicePicker</c> rather than removed from the catalog, so the server-side routers
/// and their tests still see the full id vocabulary.
/// </param>
public sealed record AiProviderOption(
    string Id,
    string DisplayName,
    string Category,
    string Hint,
    bool ExecutesInBrowser = false,
    bool DevOnly = false);
