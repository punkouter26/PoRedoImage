using Microsoft.Playwright;

namespace PoRedoImage.Tests.E2E.UI;

/// <summary>
/// C# Playwright UI tests covering the forced-login gate and the dev GUEST bypass, run across
/// the project's standardized device matrix (see <see cref="PlaywrightViewports"/>). Each fact
/// explicitly names its viewport so the failure log immediately identifies the broken device
/// class. Viewport presets are reused across the suite (item #5) — never inline a viewport
/// here; add it to <see cref="PlaywrightViewports"/> instead.
///
/// The redirects-to-login case is a single <c>[Theory]</c> with one inline row per viewport —
/// the same body runs three times, one per device class. The guest-bypass path stays as three
/// explicit <c>[Fact]</c>s because the failure mode is per-viewport (collapsed nav, touch targets)
/// and a single fact would hide which device class regressed.
/// </summary>
public sealed class LoginUiTests : IClassFixture<PlaywrightBrowserFixture>
{
    private readonly PlaywrightBrowserFixture _fixture;

    public LoginUiTests(PlaywrightBrowserFixture fixture)
    {
        _fixture = fixture;
    }

    private IBrowser _browser => _fixture.Browser;

    // ─── Unauthenticated home → /login across the viewport matrix ─────────

    [LiveServerTheory]
    [InlineData("DesktopLandscape")]
    [InlineData("MobilePortrait")]
    [InlineData("MobileLandscape")]
    public async Task Unauthenticated_home_redirects_to_login(string viewport)
    {
        var contextOptions = ViewportByName(viewport);
        await using var context = await _browser.CreateContextAsync(contextOptions);
        var page = await context.NewPageAsync();
        await page.GotoAsync(LiveServerFactAttribute.BaseUrl, new() { WaitUntil = WaitUntilState.NetworkIdle });

        // The redirect is performed by the WASM router AFTER boot, and NetworkIdle can settle
        // before that — on a cold first load the assertion used to fire while the URL was still
        // "/", which made this test intermittently fail for no real reason. Wait for the
        // navigation itself rather than assuming it has already happened.
        await page.WaitForURLAsync("**/login**", new() { Timeout = 15_000 });
        Assert.Contains("/login", page.Url, StringComparison.OrdinalIgnoreCase);
    }

    // ─── Guest bypass reaches Studio across the viewport matrix ───────────

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

    // ─── /style-director legacy alias still resolves to Studio ────────────

    [LiveServerFact]
    public async Task StyleDirector_legacy_alias_renders_Studio()
    {
        // The /style-director URL was the page's first name; an old share link could still
        // land here. The route is bound on the Studio component itself (Studio.razor) so both
        // / and /style-director render the same board. A 404 here would mean the alias was
        // dropped during a refactor.
        await using var context = await _browser.CreateContextAsync(PlaywrightViewports.DesktopLandscape());
        var page = await context.NewPageAsync();

        await page.GotoAsync(
            $"{LiveServerFactAttribute.BaseUrl}/dev-login?email=guest@guest.local",
            new() { WaitUntil = WaitUntilState.NetworkIdle });

        await page.GotoAsync(
            $"{LiveServerFactAttribute.BaseUrl}/style-director",
            new() { WaitUntil = WaitUntilState.NetworkIdle });

        Assert.DoesNotContain("/login", page.Url, StringComparison.OrdinalIgnoreCase);
        // The route is bound to Studio, so the visible heading is "Studio", not "Style Director".
        await Assertions.Expect(page.Locator("h1")).ToContainTextAsync("Studio");
    }

    private static BrowserNewContextOptions ViewportByName(string name) => name switch
    {
        "DesktopLandscape" => PlaywrightViewports.DesktopLandscape(),
        "MobilePortrait" => PlaywrightViewports.MobilePortrait(),
        "MobileLandscape" => PlaywrightViewports.MobileLandscape(),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown viewport preset."),
    };
}
