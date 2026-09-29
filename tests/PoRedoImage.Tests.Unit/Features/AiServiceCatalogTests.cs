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
    public void Catalog_offers_the_expected_options_per_capability()
    {
        // AnalyzeImage is the one capability with a real choice across all three execution
        // locations, so it is the one that would notice a provider being dropped from the catalog.
        var analyze = AiServiceCatalog.OptionsFor(AiCapability.AnalyzeImage).Select(o => o.Id).ToList();
        Assert.Contains(AiProviderIds.AzureComputerVision, analyze);
        Assert.Contains(AiProviderIds.OllamaVision, analyze);
        Assert.Contains(AiProviderIds.BrowserFlorence2, analyze);

        // These genuinely have one implementation each. EnhanceDescription used to be here
        // too, on the grounds that "browser-local text enhancement is unimplemented" — it is
        // implemented now (ImageAnalysisRequest.PrecomputedEnhancedPrompt), so it moved out.
        // GenerateImage moved IN: its second entry advertised a fast tier that was never
        // constructed (Google:Imagen3FastModel was configured nowhere), so picking it billed the
        // standard rate at the fast tier's price.
        Assert.Single(AiServiceCatalog.OptionsFor(AiCapability.SceneDetail));
        Assert.Single(AiServiceCatalog.OptionsFor(AiCapability.CreateAudio));
        Assert.Single(AiServiceCatalog.OptionsFor(AiCapability.GenerateImage));
    }

    [Fact]
    public void Dev_only_providers_are_withheld_outside_Development()
    {
        // Ollama needs a service listening on the developer's own machine. Offering it to a
        // deployed visitor advertises a choice that can only fail for them.
        var deployed = AiServiceCatalog.OptionsFor(AiCapability.AnalyzeImage, includeDevOnly: false)
            .Select(o => o.Id).ToList();
        Assert.DoesNotContain(AiProviderIds.OllamaVision, deployed);
        Assert.Contains(AiProviderIds.AzureComputerVision, deployed);

        var dev = AiServiceCatalog.OptionsFor(AiCapability.AnalyzeImage, includeDevOnly: true)
            .Select(o => o.Id).ToList();
        Assert.Contains(AiProviderIds.OllamaVision, dev);

        // A capability whose every option is dev-only must not render an empty selector.
        foreach (var capability in AiServiceCatalog.All)
        {
            Assert.NotEmpty(AiServiceCatalog.OptionsFor(capability, includeDevOnly: false));
        }
    }

    [Fact]
    public void A_browser_option_is_offered_only_where_a_local_model_can_actually_run_it()
    {
        // The rule, not a snapshot of which capabilities happen to have one today. Offering an
        // on-device option with no registered model is the failure this guards: the picker would
        // advertise free local execution and then fall through to a metered call, which is the
        // silent-degradation pattern the architecture notes single out as the worst kind of bug
        // here — the user believes they opted out of spending and the bill says otherwise.
        var expected = new Dictionary<AiCapability, LocalCapability>
        {
            [AiCapability.AnalyzeImage] = LocalCapability.Vision,
            [AiCapability.EnhanceDescription] = LocalCapability.Text,
        };

        foreach (var capability in AiServiceCatalog.All)
        {
            var browserOptions = AiServiceCatalog.OptionsFor(capability).Where(o => o.ExecutesInBrowser).ToList();

            if (!expected.TryGetValue(capability, out var localCapability))
            {
                Assert.Empty(browserOptions);
                continue;
            }

            Assert.Single(browserOptions);
            Assert.NotNull(LocalModelRegistry.DefaultFor(localCapability));
        }

        // …and the option it offers must be DERIVED from the registry entry, not restated beside
        // it. A hand-written display name or download size drifts silently the moment the model is
        // swapped, and the size is what the user reads before agreeing to the download.
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

        Assert.Equal(AiProviderIds.GeminiVision, state.Get(AiCapability.AnalyzeImage));

        state.Set(AiCapability.AnalyzeImage, AiProviderIds.BrowserFlorence2);
        Assert.Equal(AiProviderIds.BrowserFlorence2, state.Get(AiCapability.AnalyzeImage));
    }

    [Theory]
    [InlineData(AiCapability.AnalyzeImage, "Analyze image")]
    [InlineData(AiCapability.GenerateImage, "Generate image")]
    [InlineData(AiCapability.EnhanceDescription, "Enhance description & captions")]
    [InlineData(AiCapability.SceneDetail, "Scene detail (OCR)")]
    [InlineData(AiCapability.CreateAudio, "Create audio")]
    public void LabelFor_ReturnsTheRowHeading(AiCapability capability, string expected)
    {
        Assert.Equal(expected, AiServiceCatalog.LabelFor(capability));
    }
}
