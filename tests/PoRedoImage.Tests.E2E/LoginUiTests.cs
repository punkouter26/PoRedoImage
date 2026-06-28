using Microsoft.Playwright;

namespace PoRedoImage.Tests.E2E;

/// <summary>
/// C# Playwright UI tests covering the forced-login gate and the dev GUEST bypass.
/// Mobile-portrait and desktop viewports share the same logical flow; this file
/// asserts desktop default behavior. Adding per-device matrix cases is on the
/// roadmap once Playwright browsers are installed in CI (see
/// tests/PoRedoImage.Tests.E2E/bin/&lt;cfg&gt;/net10.0/playwright.ps1 install).
/// </summary>
public sealed class LoginUiTests : IAsyncLifetime
{
    private IPlaywright _playwright = default!;
    private IBrowser _browser = default!;

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
    }

    public async Task DisposeAsync()
    {
        await _browser.DisposeAsync();
        _playwright.Dispose();
    }

    [LiveServerFact]
    public async Task Unauthenticated_home_redirects_to_login()
    {
        var page = await _browser.NewPageAsync();
        await page.GotoAsync(LiveServerFactAttribute.BaseUrl, new() { WaitUntil = WaitUntilState.NetworkIdle });

        Assert.Contains("/login", page.Url, StringComparison.OrdinalIgnoreCase);
    }

    [LiveServerFact]
    public async Task Guest_bypass_reaches_studio()
    {
        var page = await _browser.NewPageAsync();

        await page.GotoAsync(
            $"{LiveServerFactAttribute.BaseUrl}/dev-login?email=guest@guest.local",
            new() { WaitUntil = WaitUntilState.NetworkIdle });

        Assert.DoesNotContain("/login", page.Url, StringComparison.OrdinalIgnoreCase);
        await Assertions.Expect(page.Locator("h1")).ToContainTextAsync("Studio");
    }
}
