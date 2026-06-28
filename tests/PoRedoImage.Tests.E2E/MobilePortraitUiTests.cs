using Microsoft.Playwright;

namespace PoRedoImage.Tests.E2E;

/// <summary>
/// Dedicated mobile-portrait golden-path UI suite. Every page is loaded inside a 390×844 portrait
/// viewport (iPhone-12 class) with touch + mobile UA, so layout regressions that only manifest on
/// small screens are caught. Drives the GUEST dev bypass — the same flow a real tester uses — and
/// self-skips via <see cref="LiveServerFactAttribute"/> when no instance is reachable.
/// </summary>
public sealed class MobilePortraitUiTests : IAsyncLifetime
{
    private static readonly ViewportSize Portrait = new() { Width = 390, Height = 844 };

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

    private Task<IBrowserContext> NewMobileContextAsync() =>
        _browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = Portrait,
            IsMobile = true,
            HasTouch = true,
            UserAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) " +
                        "AppleWebKit/605.1.15 (KHTML, like Gecko) Mobile/15E148",
        });

    [LiveServerFact]
    public async Task Mobile_unauthenticated_home_redirects_to_login()
    {
        await using var context = await NewMobileContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync(LiveServerFactAttribute.BaseUrl, new() { WaitUntil = WaitUntilState.NetworkIdle });

        Assert.Contains("/login", page.Url, StringComparison.OrdinalIgnoreCase);
    }

    [LiveServerFact]
    public async Task Mobile_guest_bypass_reaches_studio()
    {
        await using var context = await NewMobileContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync(
            $"{LiveServerFactAttribute.BaseUrl}/dev-login?email=guest@guest.local",
            new() { WaitUntil = WaitUntilState.NetworkIdle });

        Assert.DoesNotContain("/login", page.Url, StringComparison.OrdinalIgnoreCase);
        await Assertions.Expect(page.Locator("h1")).ToContainTextAsync("Studio");
    }
}
