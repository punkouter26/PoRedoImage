using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using PoRedoImage.Client.Shared;
using Radzen;
using System.Net.Http.Json;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Blazor Web App with prerender:false — the WASM runtime activates Routes and HeadOutlet
// by fully-qualified type name from server-sent descriptors, not from builder.RootComponents.
// With <IsTrimmable>true</IsTrimmable>, ILLink removes any type that has no static code
// reference, including these two. typeof() emits an ldtoken instruction that ILLink treats
// as a hard root, keeping the classes (and their transitive Razor-generated members) in the
// published assembly so Assembly.GetType("PoRedoImage.Client.Routes") succeeds at runtime.
_ = typeof(PoRedoImage.Client.Routes);
_ = typeof(Microsoft.AspNetCore.Components.Web.HeadOutlet);

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

// Procedural Web Audio micro-feedback (success / failure / tick). Zero asset cost —
// every cue is synthesized via OscillatorNode + lowpass-filtered noise in wwwroot/js/audio.js.
builder.Services.AddScoped<AudioFeedbackService>();

// BFF auth (§2): the server serializes the authenticated principal (claims only, no tokens);
// the client deserializes it into an AuthenticationStateProvider.
builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthenticationStateDeserialization();

// Mirror the server's mock-service reasons into client DI so MockDataBanner can render the
// "USING MOCK DATA" banner. The probe is anonymous and tiny; failures are swallowed so a slow
// or unreachable server never blocks app start. Returns empty in production → no banner.
try
{
    using var probe = new HttpClient
    {
        BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
        Timeout = TimeSpan.FromSeconds(3),
    };
    var reasons = await probe.GetFromJsonAsync<string[]>("api/diag/mock-status");
    foreach (var reason in reasons ?? [])
    {
        builder.Services.AddSingleton<PoRedoImage.Domain.Interfaces.IMockable>(
            new PoRedoImage.Client.Shared.RemoteMockFlag(reason));
    }
}
catch
{
    // No mock-status endpoint / offline / timeout → assume real data, render no banner.
}

await builder.Build().RunAsync();
