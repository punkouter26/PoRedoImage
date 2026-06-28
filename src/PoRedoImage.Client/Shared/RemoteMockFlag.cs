using PoRedoImage.Domain.Interfaces;

namespace PoRedoImage.Client.Shared;

/// <summary>
/// Client-side <see cref="IMockable"/> carrying a single mock reason reported by the server's
/// <c>/api/diag/mock-status</c> probe. The server's mock AI services live in the server DI
/// container, which the WASM client cannot see — so at startup the client mirrors each reported
/// reason into its own DI as one of these, letting <c>MockDataBanner</c> render normally.
/// </summary>
public sealed class RemoteMockFlag(string reason) : IMockable
{
    public string MockReason { get; } = reason;
}
