using Microsoft.Playwright;
using PoRedoImage.Shared.Configuration;

namespace PoRedoImage.Tests.E2E.UI;

/// <summary>
/// The four single-provider capabilities render as disabled selects. Asserting on that is what stops
/// a future refactor from silently making them look choosable when nothing would change.
/// </summary>
public sealed class AiServicePickerUiTests : IAsyncLifetime
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

    [LiveServerFact]
    public async Task Studio_renders_a_selector_per_capability_with_single_provider_ones_disabled()
    {
        await using var context = await _browser.CreateContextAsync(PlaywrightViewports.DesktopLandscape());
        var page = await context.NewPageAsync();

        await page.GotoAsync(
            $"{LiveServerFactAttribute.BaseUrl}/dev-login?email=guest@guest.local",
            new() { WaitUntil = WaitUntilState.NetworkIdle });

        // Guard the precondition — a redirect back to /login would otherwise let the test
        // assert against the wrong page and report a false pass.
        Assert.DoesNotContain("/login", page.Url, StringComparison.OrdinalIgnoreCase);

        await Assertions.Expect(page.Locator(".ai-picker__select")).ToHaveCountAsync(6);

        await Assertions.Expect(page.Locator("#ai-picker-AnalyzeImage")).ToBeEnabledAsync();
        await Assertions.Expect(page.Locator("#ai-picker-GenerateImage")).ToBeEnabledAsync();
        await Assertions.Expect(page.Locator("#ai-picker-EnhanceDescription")).ToBeDisabledAsync();
        await Assertions.Expect(page.Locator("#ai-picker-StyleDirector")).ToBeDisabledAsync();
        await Assertions.Expect(page.Locator("#ai-picker-SceneDetail")).ToBeDisabledAsync();
        await Assertions.Expect(page.Locator("#ai-picker-CreateAudio")).ToBeDisabledAsync();

        // Pins that the default provider is genuinely marked selected on first render rather than
        // silently falling back to whichever <option> happens to be first in markup order.
        await Assertions.Expect(page.Locator("#ai-picker-AnalyzeImage")).ToHaveValueAsync(AiProviderIds.AzureComputerVision);
    }

    public async Task DisposeAsync()
    {
        await _browser.CloseAsync();
        _playwright.Dispose();
    }
}
