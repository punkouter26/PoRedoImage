using Microsoft.Playwright;

namespace PoRedoImage.Tests.E2E.UI;

/// <summary>
/// Webcam capture, end to end: the button opens a live preview, a capture lands as the page's
/// selected image, and the stream is released afterwards.
/// </summary>
/// <remarks>
/// This class launches its OWN browser rather than using <see cref="PlaywrightBrowserFixture"/>,
/// because a synthetic camera has to be requested at launch — <c>--use-fake-device-for-media-stream</c>
/// feeds Chromium's built-in rolling test pattern to <c>getUserMedia</c>, and
/// <c>--use-fake-ui-for-media-stream</c> auto-grants the permission prompt. Both are browser-level
/// flags, so a shared headless browser started without them cannot be reused here. The cost is one
/// extra Chromium launch for one test class.
/// </remarks>
public sealed class CameraCaptureUiTests
{
    private static readonly string[] FakeCameraArgs =
    [
        "--use-fake-ui-for-media-stream",
        "--use-fake-device-for-media-stream",
    ];

    [LiveServerFact]
    public async Task Camera_capture_puts_a_photo_into_the_page()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = true,
            Args = FakeCameraArgs,
        });

        await using var context = await browser.NewContextAsync(PlaywrightViewports.DesktopLandscape());
        // Belt and braces: the fake-UI flag already auto-accepts, but an explicit grant means a
        // failure here reads as "capture broke", never as "the prompt was not answered".
        await context.GrantPermissionsAsync(["camera"]);

        var page = await context.NewPageAsync();
        await page.GotoAsync(
            $"{LiveServerFactAttribute.BaseUrl}/dev-login?email=guest@guest.local",
            new() { WaitUntil = WaitUntilState.NetworkIdle });
        await page.GotoAsync(LiveServerFactAttribute.BaseUrl,
            new() { WaitUntil = WaitUntilState.NetworkIdle });

        Assert.DoesNotContain("/login", page.Url, StringComparison.OrdinalIgnoreCase);

        // The button is rendered only after poUx.cameraAvailable() reports 'ok', so its presence
        // already proves the secure-context and getUserMedia probe passed.
        var openButton = page.Locator(".upload-panel__camera-btn");
        await Assertions.Expect(openButton).ToBeVisibleAsync(new() { Timeout = 20_000 });

        await openButton.ClickAsync();

        // A bound stream, not merely a rendered <video>: videoWidth stays 0 until frames arrive,
        // so asserting on it is what separates "the element exists" from "the camera is running".
        var video = page.Locator(".camera__video");
        await Assertions.Expect(video).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await page.WaitForFunctionAsync(
            "() => { const v = document.querySelector('.camera__video'); return v && v.srcObject && v.videoWidth > 0; }",
            null,
            new() { Timeout = 15_000 });

        // No error banner while the preview is live.
        await Assertions.Expect(page.Locator(".alert-danger")).ToHaveCountAsync(0);

        await page.Locator(".camera__controls button", new() { HasTextString = "Capture" }).ClickAsync();

        // The capture travels the same intake path as a paste, so success is the page holding an
        // image — the preview thumbnail — not merely the camera view closing.
        await Assertions.Expect(page.Locator(".preview-image")).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Assertions.Expect(video).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator(".alert-danger")).ToHaveCountAsync(0);

        // The preview is whatever imageProcessing.js re-encoded the intake to — webp at q0.85 where
        // the browser supports it, otherwise the original type. Asserting "a data: image with real
        // bytes" rather than a specific codec is the point: it proves the capture went through the
        // SAME downscale/re-encode path an uploaded file does, instead of being special-cased.
        // An empty canvas would still yield a valid data: URL, so the length is what proves pixels.
        var src = await page.Locator(".preview-image").GetAttributeAsync("src");
        Assert.NotNull(src);
        Assert.StartsWith("data:image/", src);
        Assert.True(src.Length > 5000, $"Captured image looks empty ({src.Length} chars of data URL).");
    }
}
