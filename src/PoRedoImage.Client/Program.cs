using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using PoRedoImage.Client.Shared;
using Radzen;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Hosted Blazor Web App: the root component (App/Routes) is rendered by the server host
// and hydrated here — no builder.RootComponents mount.

// HTTP client targeting the BFF host that served this app (same origin → cookies flow,
// the WASM client never handles tokens).
builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
    Timeout = TimeSpan.FromMinutes(4),
});

// Radzen UI services (Dialog, Notification, Tooltip, ContextMenu).
builder.Services.AddRadzenComponents();

// Active-image session, persisted across feature pages for the browser session.
builder.Services.AddScoped<ImageSessionService>();

// BFF auth (§2): the server serializes the authenticated principal (claims only, no tokens);
// the client deserializes it into an AuthenticationStateProvider.
builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthenticationStateDeserialization();

await builder.Build().RunAsync();
