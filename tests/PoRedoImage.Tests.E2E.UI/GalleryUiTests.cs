using System.Text.Json;
using Microsoft.Playwright;

namespace PoRedoImage.Tests.E2E.UI;

/// <summary>
/// The Gallery's Radzen controls, and specifically the guard on its destructive path: deleting is
/// permanent here (this page has no undo bar, unlike the MyImagesGallery strip), so the confirm
/// dialog is the safety net and is worth a test of its own.
/// </summary>
public sealed class GalleryUiTests : IClassFixture<PlaywrightBrowserFixture>
{
    private readonly PlaywrightBrowserFixture _fixture;

    public GalleryUiTests(PlaywrightBrowserFixture fixture) => _fixture = fixture;

    private IBrowser _browser => _fixture.Browser;

    /// <summary>
    /// A 1x1 PNG. Content does not matter — the gallery only needs a row to render a card for.
    /// </summary>
    private const string OnePixelPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    /// <summary>
    /// Signs in as a STABLE identity and seeds one image over the API, reusing the page's own
    /// cookies. <c>guest@guest.local</c> is deliberately not used: dev-login stamps a random
    /// GUEST suffix on it, so a seed and the page that reads it back would be two different users.
    /// </summary>
    private static async Task<string> SeedImageAsync(IPage page, string email, string fileName)
    {
        await page.GotoAsync($"{LiveServerFactAttribute.BaseUrl}/dev-login?email={Uri.EscapeDataString(email)}",
            new() { WaitUntil = WaitUntilState.NetworkIdle });

        // The antiforgery cookie is HttpOnly, so the token is echoed in a header — the same thing
        // AntiforgeryTokenHandler does on the client.
        var tokenResponse = await page.APIRequest.GetAsync($"{LiveServerFactAttribute.BaseUrl}/api/antiforgery/token");
        Assert.True(tokenResponse.Ok, $"Could not fetch an antiforgery token (HTTP {tokenResponse.Status}).");
        var token = JsonDocument.Parse(await tokenResponse.TextAsync()).RootElement.GetProperty("token").GetString();

        var save = await page.APIRequest.PostAsync(
            $"{LiveServerFactAttribute.BaseUrl}/api/user-images/original",
            new()
            {
                Headers = new Dictionary<string, string> { ["X-CSRF-TOKEN"] = token! },
                DataObject = new Dictionary<string, object>
                {
                    ["imageData"] = OnePixelPng,
                    ["contentType"] = "image/png",
                    ["fileName"] = fileName,
                    ["tags"] = new[] { "e2e" },
                },
            });
        Assert.True(save.Ok, $"Seeding an image failed (HTTP {save.Status}).");
        return fileName;
    }

    [LiveServerFact]
    public async Task Deleting_a_gallery_image_asks_first_and_Cancel_keeps_it()
    {
        // A per-run identity, so a parallel run or a leftover row from an earlier run cannot
        // change what this test sees.
        var email = $"e2e-gallery-{Guid.NewGuid():N}@local.test";
        var fileName = $"e2e-{Guid.NewGuid():N}.png";

        await using var context = await _browser.CreateContextAsync(PlaywrightViewports.DesktopLandscape());
        var page = await context.NewPageAsync();

        await SeedImageAsync(page, email, fileName);

        await page.GotoAsync($"{LiveServerFactAttribute.BaseUrl}/gallery",
            new() { WaitUntil = WaitUntilState.NetworkIdle });
        Assert.DoesNotContain("/login", page.Url, StringComparison.OrdinalIgnoreCase);

        var card = page.Locator(".gallery-card");
        await Assertions.Expect(card).ToHaveCountAsync(1, new() { Timeout = 20_000 });

        // The Radzen select bar renders the counts, so it also proves the seeded row was read back.
        // "All (1)", not "ALL (1)": the caps are a CSS text-transform, which does not change the
        // text node Playwright matches against.
        await Assertions.Expect(page.Locator(".rz-selectbutton .rz-button").First).ToContainTextAsync("All (1)");

        await page.Locator(".gallery-card__actions [aria-label='Delete image']").ClickAsync();

        // DialogService.Confirm — the guard that did not exist before. Without it this click alone
        // would already have destroyed the image.
        var dialog = page.Locator(".rz-dialog");
        await Assertions.Expect(dialog).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Assertions.Expect(dialog).ToContainTextAsync("cannot be undone");

        await dialog.GetByText("Cancel").ClickAsync();

        // Cancelling must be a no-op, not a deferred delete.
        await Assertions.Expect(dialog).ToHaveCountAsync(0, new() { Timeout = 10_000 });
        await Assertions.Expect(card).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator(".gallery-card")).ToContainTextAsync(fileName);
    }
}
