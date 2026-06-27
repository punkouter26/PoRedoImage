using Microsoft.Playwright;

namespace PoRedoImage.Tests.E2EUI;

/// <summary>
/// C# Playwright UI tests. Verifies the forced-login gate: an unauthenticated visit to the
/// home page lands on the login screen, and (in dev/test) the GUEST bypass reaches the studio.
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

        // The forced-login gate must send anonymous users to /login.
        Assert.Contains("/login", page.Url, StringComparison.OrdinalIgnoreCase);
    }

    [LiveServerFact]
    public async Task Guest_bypass_reaches_studio()
    {
        var page = await _browser.NewPageAsync();

        // Dev/Test GUEST bypass: signs in and redirects to the studio home.
        await page.GotoAsync(
            $"{LiveServerFactAttribute.BaseUrl}/dev-login?email=guest@guest.local",
            new() { WaitUntil = WaitUntilState.NetworkIdle });

        Assert.DoesNotContain("/login", page.Url, StringComparison.OrdinalIgnoreCase);
        await Assertions.Expect(page.Locator("h1")).ToContainTextAsync("Studio");
    }
}
