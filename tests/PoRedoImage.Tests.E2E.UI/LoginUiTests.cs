using Microsoft.Playwright;

namespace PoRedoImage.Tests.E2E.UI;

/// <summary>
/// C# Playwright UI tests covering the forced-login gate and the dev GUEST bypass, run across
/// the project's standardized device matrix (see <see cref="PlaywrightViewports"/>). Each fact
/// explicitly names its viewport so the failure log immediately identifies the broken device
/// class. Viewport presets are reused across the suite (item #5) — never inline a viewport
/// here; add it to <see cref="PlaywrightViewports"/> instead.
///
/// Three logical flows × three viewports = 6 facts. Well under the 25-fact UI ceiling
/// (<see cref="TestCountCeilingTests.UiCeiling"/>), so adding new flows is cheap.
/// </summary>
public sealed class LoginUiTests : IAsyncLifetime
{
    private IPlaywright _playwright = default!;
    private IBrowser _browser = default!;

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });

        await WarmUpAsync();
    }

    /// <summary>
    /// Loads the app once and waits for the WASM runtime to boot before any test asserts.
    /// </summary>
    /// <remarks>
    /// The first navigation after a rebuild pays for downloading and starting the WASM runtime,
    /// which can outlast <c>NetworkIdle</c>. Tests that asserted immediately after it were
    /// intermittently checking a page that had not finished hydrating — a flake that only appeared
    /// on the first run after a build and vanished on re-run, which is the worst kind to chase.
    /// Paying that cost once here makes every subsequent navigation hit a warm runtime.
    /// </remarks>
    private async Task WarmUpAsync()
    {
        await using var context = await _browser.NewContextAsync();
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
        await _browser.DisposeAsync();
        _playwright.Dispose();
    }

    // ─── Desktop landscape (1920×1080) ───────────────────────────────

    [LiveServerFact]
    public async Task DesktopLandscape_Unauthenticated_home_redirects_to_login()
    {
        await using var context = await _browser.CreateContextAsync(PlaywrightViewports.DesktopLandscape());
        var page = await context.NewPageAsync();
        await page.GotoAsync(LiveServerFactAttribute.BaseUrl, new() { WaitUntil = WaitUntilState.NetworkIdle });

        // The redirect is performed by the WASM router AFTER boot, and NetworkIdle can settle
        // before that — on a cold first load the assertion used to fire while the URL was still
        // "/", which made this test intermittently fail for no real reason. Wait for the
        // navigation itself rather than assuming it has already happened.
        await page.WaitForURLAsync("**/login**", new() { Timeout = 15_000 });
        Assert.Contains("/login", page.Url, StringComparison.OrdinalIgnoreCase);
    }

    [LiveServerFact]
    public async Task DesktopLandscape_Guest_bypass_reaches_studio()
    {
        await using var context = await _browser.CreateContextAsync(PlaywrightViewports.DesktopLandscape());
        var page = await context.NewPageAsync();

        await page.GotoAsync(
            $"{LiveServerFactAttribute.BaseUrl}/dev-login?email=guest@guest.local",
            new() { WaitUntil = WaitUntilState.NetworkIdle });

        Assert.DoesNotContain("/login", page.Url, StringComparison.OrdinalIgnoreCase);
        await Assertions.Expect(page.Locator("h1")).ToContainTextAsync("Studio");
    }

    // ─── Mobile portrait (390×844, iPhone-12 class) ─────────────────

    [LiveServerFact]
    public async Task MobilePortrait_Unauthenticated_home_redirects_to_login()
    {
        await using var context = await _browser.CreateContextAsync(PlaywrightViewports.MobilePortrait());
        var page = await context.NewPageAsync();
        await page.GotoAsync(LiveServerFactAttribute.BaseUrl, new() { WaitUntil = WaitUntilState.NetworkIdle });

        // The redirect is performed by the WASM router AFTER boot, and NetworkIdle can settle
        // before that — on a cold first load the assertion used to fire while the URL was still
        // "/", which made this test intermittently fail for no real reason. Wait for the
        // navigation itself rather than assuming it has already happened.
        await page.WaitForURLAsync("**/login**", new() { Timeout = 15_000 });
        Assert.Contains("/login", page.Url, StringComparison.OrdinalIgnoreCase);
    }

    [LiveServerFact]
    public async Task MobilePortrait_Guest_bypass_reaches_studio()
    {
        await using var context = await _browser.CreateContextAsync(PlaywrightViewports.MobilePortrait());
        var page = await context.NewPageAsync();

        await page.GotoAsync(
            $"{LiveServerFactAttribute.BaseUrl}/dev-login?email=guest@guest.local",
            new() { WaitUntil = WaitUntilState.NetworkIdle });

        Assert.DoesNotContain("/login", page.Url, StringComparison.OrdinalIgnoreCase);
        await Assertions.Expect(page.Locator("h1")).ToContainTextAsync("Studio");
    }

    // ─── Mobile landscape (844×390, iPhone-12 rotated) ──────────────

    [LiveServerFact]
    public async Task MobileLandscape_Unauthenticated_home_redirects_to_login()
    {
        await using var context = await _browser.CreateContextAsync(PlaywrightViewports.MobileLandscape());
        var page = await context.NewPageAsync();
        await page.GotoAsync(LiveServerFactAttribute.BaseUrl, new() { WaitUntil = WaitUntilState.NetworkIdle });

        // The redirect is performed by the WASM router AFTER boot, and NetworkIdle can settle
        // before that — on a cold first load the assertion used to fire while the URL was still
        // "/", which made this test intermittently fail for no real reason. Wait for the
        // navigation itself rather than assuming it has already happened.
        await page.WaitForURLAsync("**/login**", new() { Timeout = 15_000 });
        Assert.Contains("/login", page.Url, StringComparison.OrdinalIgnoreCase);
    }

    [LiveServerFact]
    public async Task MobileLandscape_Guest_bypass_reaches_studio()
    {
        await using var context = await _browser.CreateContextAsync(PlaywrightViewports.MobileLandscape());
        var page = await context.NewPageAsync();

        await page.GotoAsync(
            $"{LiveServerFactAttribute.BaseUrl}/dev-login?email=guest@guest.local",
            new() { WaitUntil = WaitUntilState.NetworkIdle });

        Assert.DoesNotContain("/login", page.Url, StringComparison.OrdinalIgnoreCase);
        await Assertions.Expect(page.Locator("h1")).ToContainTextAsync("Studio");
    }
}
