using Microsoft.Playwright;

namespace PoRedoImage.Tests.E2E.UI;

/// <summary>
/// Centralized Playwright <see cref="BrowserNewContextOptions"/> per device class. Standardizes
/// the mobile-first &amp; cross-browser testing matrix (item #5): every UI suite uses one of these
/// presets so viewports stay consistent across files and the matrix is auditable in one place.
///
/// Three presets cover the user-spec device classes:
///   - <see cref="MobilePortrait"/>   — 390×844, iPhone-12 class, touch + mobile UA.
///   - <see cref="MobileLandscape"/>  — 844×390, same iPhone rotated, catches landscape regressions.
///   - <see cref="DesktopLandscape"/> — 1920×1080, desktop PC viewport in landscape orientation.
///
/// Always created via <see cref="CreateContextAsync"/> so the lifetime/disposal pattern is shared
/// across the test files (no leaked <c>IBrowserContext</c>).
/// </summary>
public static class PlaywrightViewports
{
    public static readonly ViewportSize MobilePortraitSize = new() { Width = 390, Height = 844 };
    public static readonly ViewportSize MobileLandscapeSize = new() { Width = 844, Height = 390 };
    public static readonly ViewportSize DesktopLandscapeSize = new() { Width = 1920, Height = 1080 };

    public static BrowserNewContextOptions MobilePortrait() => new()
    {
        ViewportSize = MobilePortraitSize,
        IsMobile = true,
        HasTouch = true,
        UserAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) " +
                    "AppleWebKit/605.1.15 (KHTML, like Gecko) Mobile/15E148",
    };

    public static BrowserNewContextOptions MobileLandscape() => new()
    {
        ViewportSize = MobileLandscapeSize,
        IsMobile = true,
        HasTouch = true,
        UserAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) " +
                    "AppleWebKit/605.1.15 (KHTML, like Gecko) Mobile/15E148",
    };

    public static BrowserNewContextOptions DesktopLandscape() => new()
    {
        ViewportSize = DesktopLandscapeSize,
        IsMobile = false,
        HasTouch = false,
        // Default desktop UA — Chromium picks one when null, so leave it null and document the
        // intent: "real desktop browser, not mobile-emulated".
    };

    /// <summary>
    /// Convenience wrapper: callers always get a context in an <c>await using</c> block so the
    /// disposal contract is identical across the matrix.
    /// </summary>
    public static Task<IBrowserContext> CreateContextAsync(this IBrowser browser, BrowserNewContextOptions options)
        => browser.NewContextAsync(options);
}
