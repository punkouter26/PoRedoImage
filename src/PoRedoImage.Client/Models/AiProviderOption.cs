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
public sealed record AiProviderOption(
    string Id,
    string DisplayName,
    string Category,
    string Hint,
    bool ExecutesInBrowser = false);
