using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;

namespace PoRedoImage.Tests.E2E.UI;

/// <summary>
/// Automated WCAG 2.2 Level AA gate (§4 "WCAG 2.2 Level AA compliance on all interactive elements").
/// </summary>
/// <remarks>
/// The suite previously relied on hand-written <c>aria-*</c> attributes with nothing verifying them.
/// axe-core is run against the two pages every user necessarily passes through — the anonymous login
/// gate and the authenticated Studio home — and fails on any <c>violation</c> tagged wcag2a, wcag2aa,
/// wcag21aa, or wcag22aa.
/// <para>
/// axe reports "violations" (deterministic failures) separately from "incomplete" (needs review).
/// Only violations fail the build: incomplete results include contrast checks axe cannot resolve
/// against a gradient or image background, and gating on them would make the suite flaky rather than
/// strict. Class name contains "Ui" so <c>TestCountCeilingTests</c> partitions it into the UI tier.
/// </para>
/// </remarks>
public sealed class AccessibilityUiTests : IAsyncLifetime
{
    private static readonly string[] WcagAaTags = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"];

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

    [LiveServerFact]
    public async Task Login_page_has_no_WCAG_AA_violations()
    {
        await using var context = await _browser.CreateContextAsync(PlaywrightViewports.DesktopLandscape());
        var page = await context.NewPageAsync();
        await page.GotoAsync(
            $"{LiveServerFactAttribute.BaseUrl}/login",
            new() { WaitUntil = WaitUntilState.NetworkIdle });

        await AssertNoViolationsAsync(page);
    }

    [LiveServerFact]
    public async Task Studio_page_has_no_WCAG_AA_violations()
    {
        await using var context = await _browser.CreateContextAsync(PlaywrightViewports.DesktopLandscape());
        var page = await context.NewPageAsync();
        await page.GotoAsync(
            $"{LiveServerFactAttribute.BaseUrl}/dev-login?email=guest@guest.local",
            new() { WaitUntil = WaitUntilState.NetworkIdle });

        // Guard the precondition: a redirect back to /login would otherwise let this scan the
        // login page a second time and report a false pass for Studio.
        Assert.DoesNotContain("/login", page.Url, StringComparison.OrdinalIgnoreCase);

        // The model picker renders only after LocalAiService probes the GPU over JS interop, so
        // its presence also proves poLocalAiProbeDevice loaded and returned without throwing.
        await Assertions.Expect(page.Locator("legend", new() { HasTextString = "AI model" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("Web Browser")).ToBeVisibleAsync();

        await AssertNoViolationsAsync(page);
    }

    [LiveServerFact]
    public async Task Rap_roast_page_has_no_WCAG_AA_violations()
    {
        await using var context = await _browser.CreateContextAsync(PlaywrightViewports.DesktopLandscape());
        var page = await context.NewPageAsync();

        await page.GotoAsync(
            $"{LiveServerFactAttribute.BaseUrl}/dev-login?email=guest@guest.local",
            new() { WaitUntil = WaitUntilState.NetworkIdle });
        await page.GotoAsync(
            $"{LiveServerFactAttribute.BaseUrl}/rap-roast",
            new() { WaitUntil = WaitUntilState.NetworkIdle });

        Assert.DoesNotContain("/login", page.Url, StringComparison.OrdinalIgnoreCase);
        // The radio-group fieldset and the audio player are the parts most likely to regress here.
        await Assertions.Expect(page.Locator("legend")).ToContainTextAsync("Beat style");

        await AssertNoViolationsAsync(page);
    }

    [LiveServerFact]
    public async Task Studio_page_has_no_WCAG_AA_violations_on_mobile_portrait()
    {
        // Mobile portrait is the project's primary form factor and collapses the nav, so it can
        // regress independently of desktop — a hidden-but-focusable control, for instance.
        await using var context = await _browser.CreateContextAsync(PlaywrightViewports.MobilePortrait());
        var page = await context.NewPageAsync();
        await page.GotoAsync(
            $"{LiveServerFactAttribute.BaseUrl}/dev-login?email=guest@guest.local",
            new() { WaitUntil = WaitUntilState.NetworkIdle });

        Assert.DoesNotContain("/login", page.Url, StringComparison.OrdinalIgnoreCase);

        await AssertNoViolationsAsync(page);
    }

    private static async Task AssertNoViolationsAsync(IPage page)
    {
        var results = await page.RunAxe(new AxeRunOptions
        {
            RunOnly = new RunOnlyOptions { Type = "tag", Values = [.. WcagAaTags] },
        });

        if (results.Violations.Length == 0)
        {
            return;
        }

        // Include each check's message, not just the element HTML: for color-contrast axe reports
        // the measured ratio and the exact foreground/background pair, which is the only thing that
        // makes a failure actionable without re-deriving computed styles by hand.
        var report = string.Join(
            Environment.NewLine,
            results.Violations.Select(v =>
                $"  [{v.Impact}] {v.Id}: {v.Help}{Environment.NewLine}" +
                $"    {v.HelpUrl}{Environment.NewLine}" +
                string.Join(Environment.NewLine, v.Nodes.Select(n =>
                    $"    → {n.Html}{Environment.NewLine}" +
                    string.Join(Environment.NewLine,
                        (n.Any ?? []).Select(c => $"        {c.Message}"))))));

        Assert.Fail(
            $"{results.Violations.Length} WCAG 2.2 AA violation(s) on {page.Url}:{Environment.NewLine}{report}");
    }
}
