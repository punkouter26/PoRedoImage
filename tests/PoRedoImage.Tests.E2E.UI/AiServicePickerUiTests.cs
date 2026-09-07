using Microsoft.Playwright;
using PoRedoImage.Shared.Configuration;

namespace PoRedoImage.Tests.E2E.UI;

/// <summary>
/// Single-provider capabilities render as disabled selects. Asserting on that is what stops a future
/// refactor from silently making them look choosable when nothing would change.
/// </summary>
public sealed class AiServicePickerUiTests : IClassFixture<PlaywrightBrowserFixture>
{
    /// <summary>
    /// The <c>AiCapability</c> names the picker renders, in order. Kept as strings because this test
    /// project deliberately does not reference the Client assembly — it drives the app over HTTP like
    /// any other browser, and the element ids are the contract.
    /// </summary>
    private static readonly string[] AiCapabilityNames =
    [
        "AnalyzeImage", "GenerateImage", "EnhanceDescription",
        "SceneDetail", "CreateAudio",
    ];

    private readonly PlaywrightBrowserFixture _fixture;

    public AiServicePickerUiTests(PlaywrightBrowserFixture fixture)
    {
        _fixture = fixture;
    }

    private IBrowser _browser => _fixture.Browser;

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

        // Wait for Blazor WASM to hydrate and render the interactive selectors
        await Assertions.Expect(page.Locator(".ai-picker__select").First).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // At least two: AnalyzeImage and EnhanceDescription. GenerateImage used to make a third,
        // on the strength of a "Gemini Imagen 3 Fast" entry that never ran — the fast tier was only
        // constructed when Google:Imagen3FastModel was configured, and it was configured nowhere, so
        // the option resolved to the standard service while advertising half its price. What this
        // test actually protects is the invariant asserted in the loop below: every selector the
        // picker renders offers a real choice.
        var choosableSelects = page.Locator(".ai-picker__select");
        var selectCount = await choosableSelects.CountAsync();
        Assert.True(selectCount >= 2, $"Expected at least 2 choosable selectors, found {selectCount}.");

        for (int i = 0; i < selectCount; i++)
        {
            var select = choosableSelects.Nth(i);
            await Assertions.Expect(select).ToBeEnabledAsync();
            var optionCount = await select.Locator("option").CountAsync();
            Assert.True(optionCount > 1, "Rendered selector should have multiple options to choose from.");
        }

        // Fixed single-provider capabilities are rendered in the summary/details section
        var fixedSummary = page.Locator(".ai-picker__fixed");
        await Assertions.Expect(fixedSummary).ToBeVisibleAsync();

        // Pins that the default provider is genuinely marked selected on first render
        await Assertions.Expect(page.Locator("#ai-picker-AnalyzeImage")).ToHaveValueAsync(AiProviderIds.AzureComputerVision);
    }
}
