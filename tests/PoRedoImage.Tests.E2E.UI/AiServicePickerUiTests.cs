using Microsoft.Playwright;

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
        await Assertions.Expect(page.Locator("#ai-picker-AnalyzeImage")).ToHaveValueAsync("remote:azure-cv");
    }

    public async Task DisposeAsync()
    {
        await _browser.CloseAsync();
        _playwright.Dispose();
    }
}
