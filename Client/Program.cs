using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components;
using Client;
using Client.Services;
using Radzen;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Configure HttpClient for API access
var baseAddress = builder.HostEnvironment.BaseAddress;
builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(baseAddress),
    Timeout = TimeSpan.FromMinutes(3) // Extended timeout for image processing
});

// Configure ApiService
builder.Services.AddScoped<ApiService>();

// Add Radzen Blazor services
builder.Services.AddRadzenComponents();

await builder.Build().RunAsync();
