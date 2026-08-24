using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using PoRedoImage.Client.Http;
using PoRedoImage.Client.Services;
using PoRedoImage.Client.Shared;
using Radzen;
using System.Net.Http.Json;
using PoRedoImage.Shared.Json;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Hosted Blazor Web App: the root component (App/Routes) is rendered by the server host
// and hydrated here — no builder.RootComponents mount. Component preservation under
// publish-trimming is handled by NOT marking this assembly IsTrimmable (see csproj).

// Stable per-tab session identity; the handler stamps X-Session-ID / X-Correlation-ID (§6.9).
builder.Services.AddSingleton<ClientRequestContext>();

// HTTP client targeting the BFF host that served this app (same origin → cookies flow,
// the WASM client never handles tokens). The correlation handler wraps the browser fetch
// handler so every BFF call carries session + correlation headers for end-to-end tracing.
// The antiforgery handler sits between correlation and the browser fetch handler so a replayed
// request (stale-token retry) keeps its correlation headers and gets a fresh CSRF token (§2).
builder.Services.AddScoped(sp =>
{
    var baseAddress = new Uri(builder.HostEnvironment.BaseAddress);

    return new HttpClient(
        new CorrelationHeaderHandler(sp.GetRequiredService<ClientRequestContext>())
        {
            InnerHandler = new AntiforgeryTokenHandler(
                // A bare client for the token GET: reusing this pipeline would re-enter the
                // handler that is mid-fetch.
                () => new HttpClient { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(30) })
            {
                InnerHandler = new HttpClientHandler()
            }
        })
    {
        BaseAddress = baseAddress,
        Timeout = TimeSpan.FromMinutes(4),
    };
});

// Radzen UI services (Dialog, Notification, Tooltip, ContextMenu).
builder.Services.AddRadzenComponents();

// Active-image session, persisted across feature pages for the browser session.
builder.Services.AddScoped<ImageSessionService>();

// Browser-native local AI (§5): model registry + dtype fallback chain, executed on a worker.
// Scoped so each circuit disposes its workers and releases GPU memory.
builder.Services.AddScoped<PoRedoImage.Client.LocalAi.LocalAiService>();

// Per-capability provider selection (§ai-service-pickers): session-only, resets on reload so a
// removed provider can never leave a stale selection stranded.
builder.Services.AddScoped<PoRedoImage.Client.Models.AiSelectionState>();

// Procedural Web Audio micro-feedback (success / failure / tick). Zero asset cost —
// every cue is synthesized via OscillatorNode + lowpass-filtered noise in wwwroot/js/audio.js.
builder.Services.AddScoped<AudioFeedbackService>();

// Gallery saver (PO saves): idempotency-keyed POST + Radzen retry toast. Scoped per circuit
// so each user session has its own retry handler (TaskCompletionSource held in notifications).
builder.Services.AddScoped<UserImageSaveService>();

// Recently-used / pinned art-style prompts, persisted to localStorage by the prompt drawer.
builder.Services.AddScoped<PromptHistoryService>();

// Running per-session image-generation cost estimate, seeded from GET /api/pricing. Scoped so
// the tally resets with the app instance — it describes this visit, not the account.
builder.Services.AddScoped<SessionCostService>();

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
    var reasons = await probe.GetFromJsonAsync<string[]>("api/diag/mock-status", SharedJsonOptions.Default);
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
