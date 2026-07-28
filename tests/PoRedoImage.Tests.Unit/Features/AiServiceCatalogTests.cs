using System.Net;
using PoRedoImage.Client.LocalAi;
using PoRedoImage.Client.Models;
using PoRedoImage.Shared.Configuration;

namespace PoRedoImage.Tests.Unit.Features;

/// <summary>
/// The catalog is the single source of truth the picker renders from. Browser options must be
/// derived from LocalModelRegistry rather than restated, or the two lists drift.
/// </summary>
public class AiServiceCatalogTests
{
    [Fact]
    public void AnalyzeImage_OffersRemoteOllamaAndBrowser()
    {
        var ids = AiServiceCatalog.OptionsFor(AiCapability.AnalyzeImage).Select(o => o.Id).ToList();

        Assert.Contains(AiProviderIds.AzureComputerVision, ids);
        Assert.Contains(AiProviderIds.OllamaVision, ids);
        Assert.Contains(AiProviderIds.BrowserFlorence2, ids);
    }

    [Fact]
    public void SingleProviderCapabilities_HaveExactlyOneOption()
    {
        // EnhanceDescription is single-provider: browser-local text enhancement is unimplemented,
        // so offering Qwen2.5 here would claim a capability the code does not have.
        Assert.Single(AiServiceCatalog.OptionsFor(AiCapability.EnhanceDescription));
        Assert.Single(AiServiceCatalog.OptionsFor(AiCapability.StyleDirector));
        Assert.Single(AiServiceCatalog.OptionsFor(AiCapability.SceneDetail));
        Assert.Single(AiServiceCatalog.OptionsFor(AiCapability.CreateAudio));
    }

    [Fact]
    public void AnalyzeImage_IsTheOnlyCapabilityWithABrowserOption()
    {
        var withBrowser = AiServiceCatalog.All
            .Where(c => AiServiceCatalog.OptionsFor(c).Any(o => o.ExecutesInBrowser))
            .ToList();

        Assert.Equal([AiCapability.AnalyzeImage], withBrowser);
    }

    [Fact]
    public void BrowserOption_MirrorsTheLocalModelRegistry()
    {
        var florence = LocalModelRegistry.DefaultFor(LocalCapability.Vision);
        Assert.NotNull(florence);

        var option = AiServiceCatalog.OptionsFor(AiCapability.AnalyzeImage)
            .Single(o => o.Id == AiProviderIds.BrowserFlorence2);

        Assert.True(option.ExecutesInBrowser);
        Assert.Contains(florence.DisplayName, option.DisplayName);
        Assert.Contains($"{florence.ApproxDownloadMb} MB", option.Hint);
    }

    [Fact]
    public void SelectionState_ReturnsCatalogDefaultUntilOverridden()
    {
        // HttpClient is only ever touched by EnsureInitializedAsync, which this test never calls —
        // a plain instance (no BaseAddress) is enough to satisfy the constructor.
        var state = new AiSelectionState(new HttpClient());

        Assert.Equal(AiProviderIds.AzureComputerVision, state.Get(AiCapability.AnalyzeImage));

        state.Set(AiCapability.AnalyzeImage, AiProviderIds.BrowserFlorence2);
        Assert.Equal(AiProviderIds.BrowserFlorence2, state.Get(AiCapability.AnalyzeImage));
    }

    [Theory]
    [InlineData(AiCapability.AnalyzeImage, "Analyze image")]
    [InlineData(AiCapability.GenerateImage, "Generate image")]
    [InlineData(AiCapability.EnhanceDescription, "Enhance description & captions")]
    [InlineData(AiCapability.StyleDirector, "Style Director")]
    [InlineData(AiCapability.SceneDetail, "Scene detail (OCR)")]
    [InlineData(AiCapability.CreateAudio, "Create audio")]
    public void LabelFor_ReturnsTheRowHeading(AiCapability capability, string expected)
    {
        Assert.Equal(expected, AiServiceCatalog.LabelFor(capability));
    }

    /// <summary>
    /// Regression guard for the branch's Critical defect: <c>AiSelectionState.GetExplicit</c> is the
    /// one thing that must never fall back to a guess, because <c>FeaturePageBase</c> stamps its
    /// result straight onto <c>ImageAnalysisRequest.ImageGenModelId</c> and the server-side router
    /// treats an explicit id as an instruction, not a hint. Drives <see cref="AiSelectionState"/>
    /// through a stub <see cref="HttpMessageHandler"/> instead of a real network call, exercising
    /// every branch of <c>SeedImageGenDefaultAsync</c>'s provider-key mapping plus the rule that an
    /// explicit <see cref="AiSelectionState.Set"/> always wins over any seeded value.
    /// </summary>
    [Fact]
    public async Task GetExplicit_SeedsFromPricingButNeverOverridesAnExplicitChoice()
    {
        // Recognised provider key ("google") seeds the matching provider id.
        var seeded = NewState(HttpStatusCode.OK, """{"imageProvider":"google","imageProviderLabel":"Google","textToImageUsd":0.039,"imageToImageUsd":0.039,"currency":"USD"}""");
        await seeded.EnsureInitializedAsync();
        Assert.Equal(AiProviderIds.GeminiImagen3, seeded.GetExplicit(AiCapability.GenerateImage));

        // A failed fetch (HTTP 500) degrades to null rather than guessing.
        var seedFailed = NewState(HttpStatusCode.InternalServerError, null);
        await seedFailed.EnsureInitializedAsync();
        Assert.Null(seedFailed.GetExplicit(AiCapability.GenerateImage));

        // An unrecognised provider key also degrades to null rather than guessing.
        var unrecognised = NewState(HttpStatusCode.OK, """{"imageProvider":"openai","imageProviderLabel":"OpenAI","textToImageUsd":0,"imageToImageUsd":0,"currency":"USD"}""");
        await unrecognised.EnsureInitializedAsync();
        Assert.Null(unrecognised.GetExplicit(AiCapability.GenerateImage));

        // An explicit user choice always wins over a successfully seeded default.
        var explicitChoice = NewState(HttpStatusCode.OK, """{"imageProvider":"google","imageProviderLabel":"Google","textToImageUsd":0.039,"imageToImageUsd":0.039,"currency":"USD"}""");
        await explicitChoice.EnsureInitializedAsync();
        // GenerateImage only has one provider now (Gemini), so a Set on a non-existent id is
        // functionally a no-op for routing. Set+GetExplicit still round-trips the explicit choice.
        explicitChoice.Set(AiCapability.GenerateImage, AiProviderIds.GeminiImagen3);
        Assert.Equal(AiProviderIds.GeminiImagen3, explicitChoice.GetExplicit(AiCapability.GenerateImage));
    }

    private static AiSelectionState NewState(HttpStatusCode statusCode, string? jsonBody)
    {
        var http = new HttpClient(new StubHttpMessageHandler(statusCode, jsonBody))
        {
            BaseAddress = new Uri("http://localhost/"),
        };
        return new AiSelectionState(http);
    }

    /// <summary>Returns a fixed status code and JSON body for every request, regardless of URI.</summary>
    private sealed class StubHttpMessageHandler(HttpStatusCode statusCode, string? jsonBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode);
            if (jsonBody is not null)
            {
                response.Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");
            }

            return Task.FromResult(response);
        }
    }

    [Fact]
    public void All_ListsEveryCapability_InRenderOrder()
    {
        // Order is contractual: it is the order the picker renders its rows. Deriving it from
        // Dictionary.Keys would leave it at the mercy of an unspecified enumeration order.
        Assert.Equal(
            [
                AiCapability.AnalyzeImage,
                AiCapability.GenerateImage,
                AiCapability.EnhanceDescription,
                AiCapability.StyleDirector,
                AiCapability.SceneDetail,
                AiCapability.CreateAudio,
            ],
            AiServiceCatalog.All);

        // Closes the catalog/enum drift hole: the hardcoded list above passes even if a new
        // AiCapability member is added to the enum and to Catalog but forgotten here — it would
        // simply render nothing. Comparing against Enum.GetValues (order-independent; order is
        // already pinned above) catches that.
        Assert.Equal(
            Enum.GetValues<AiCapability>().ToHashSet(),
            AiServiceCatalog.All.ToHashSet());
    }
}
