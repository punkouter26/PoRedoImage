using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace PoRedoImage.Client;

/// <summary>
/// Shared render modes. <see cref="WasmNoPrerender"/> runs pages purely client-side with
/// prerendering disabled: the WASM client owns the only DI container (IWebAssemblyHostEnvironment,
/// ImageSessionService, the deserialized auth state), so server-side prerender would fail to
/// resolve those client-only services. Auth is enforced client-side (AuthorizeRouteView → /login)
/// and server-side on every /api endpoint.
/// </summary>
public static class RenderModes
{
    public static readonly IComponentRenderMode WasmNoPrerender =
        new InteractiveWebAssemblyRenderMode(prerender: false);
}
