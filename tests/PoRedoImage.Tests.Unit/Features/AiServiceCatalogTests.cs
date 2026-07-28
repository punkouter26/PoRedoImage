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
        var state = new AiSelectionState();

        Assert.Equal(AiProviderIds.AzureComputerVision, state.Get(AiCapability.AnalyzeImage));

        state.Set(AiCapability.AnalyzeImage, AiProviderIds.BrowserFlorence2);
        Assert.Equal(AiProviderIds.BrowserFlorence2, state.Get(AiCapability.AnalyzeImage));
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
    }
}
