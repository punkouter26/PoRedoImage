using Microsoft.Playwright;

namespace PoRedoImage.Tests.E2E.UI;

/// <summary>
/// Shared Playwright browser fixture for the E2E UI test suite.
/// Boots Chromium once per test class and pre-warms the WASM runtime.
/// </summary>
public sealed class PlaywrightBrowserFixture : IAsyncLifetime
{
    public IPlaywright PlaywrightInstance { get; private set; } = default!;
    public IBrowser Browser { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        PlaywrightInstance = await Playwright.CreateAsync();
        Browser = await PlaywrightInstance.Chromium.LaunchAsync(new() { Headless = true });

        await WarmUpAsync();
    }

    /// <summary>
    /// Loads the app once and waits for the WASM runtime to boot before any test asserts.
    /// </summary>
    private async Task WarmUpAsync()
    {
        await using var context = await Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync(LiveServerFactAttribute.BaseUrl, new() { WaitUntil = WaitUntilState.NetworkIdle });
            // The login form only renders once Blazor has hydrated, so its presence is the signal
            // that the runtime is actually up rather than merely downloaded.
            await page.WaitForSelectorAsync("a[href*='login'], .login-card, h1",
                new() { Timeout = 30_000 });
        }
        catch (TimeoutException)
        {
            // Warm-up is an optimisation. If it times out the tests still run and report honestly.
        }
    }

    public async Task DisposeAsync()
    {
        if (Browser != null)
        {
            await Browser.DisposeAsync();
        }
        PlaywrightInstance?.Dispose();
    }
}
