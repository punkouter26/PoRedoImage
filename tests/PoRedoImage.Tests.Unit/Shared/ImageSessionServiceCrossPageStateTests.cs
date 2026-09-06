using PoRedoImage.Client.Shared;

namespace PoRedoImage.Tests.Unit.Shared;

/// <summary>
/// Locks the cross-page state contract of <see cref="ImageSessionService"/>:
/// <c>LastFinalPrompt</c> + <c>StagedPrompt</c> + <c>OnChange</c> wiring.
///
/// The previous <c>LastVisitedFeatureRoute</c> property was removed: nothing in the codebase
/// reads it (a grep across <c>src/**</c> finds only the writer and the clearer). The XML doc
/// on the property said it was for the "Active Image Bar", which was itself removed. Tests
/// that asserted on it have been deleted as part of the same change. The remaining tests pin
/// the contract that IS still consumed: the final prompt that <c>ResultsPanel</c> re-seeds
/// from on a fresh visit, and the one-shot staged prompt that flows between pages.
/// </summary>
public sealed class ImageSessionServiceCrossPageStateTests
{
    [Fact]
    public void Defaults_are_null_until_recorded()
    {
        var sut = new ImageSessionService();
        Assert.Null(sut.LastFinalPrompt);
        Assert.Null(sut.StagedPrompt);
    }

    [Fact]
    public void RecordFeatureVisit_sets_prompt_only()
    {
        // The route argument is now ignored — it was only ever stored in a property no reader
        // consulted. The prompt is the actual cross-page payload.
        var sut = new ImageSessionService();
        sut.RecordFeatureVisit("/bulk-generate", "studio noir, 16:9");
        Assert.Equal("studio noir, 16:9", sut.LastFinalPrompt);
    }

    [Fact]
    public void RecordFeatureVisit_keeps_previous_prompt_when_null()
    {
        var sut = new ImageSessionService();
        sut.RecordFeatureVisit("/studio", "first prompt");
        sut.RecordFeatureVisit("/meme-generation"); // no prompt supplied
        Assert.Equal("first prompt", sut.LastFinalPrompt);
    }

    [Fact]
    public void RecordFeatureVisit_fires_OnChange()
    {
        var sut = new ImageSessionService();
        var fired = 0;
        sut.OnChange += () => fired++;
        sut.RecordFeatureVisit("/studio", "hello");
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Clear_resets_prompt_and_staged()
    {
        var sut = new ImageSessionService();
        sut.RecordFeatureVisit("/bulk-generate", "lots of cats");
        sut.StagePrompt("from style director");
        sut.Clear();
        Assert.Null(sut.LastFinalPrompt);
        Assert.Null(sut.StagedPrompt);
    }

    [Fact]
    public void SetImage_does_not_touch_prompt_or_staged()
    {
        var sut = new ImageSessionService();
        sut.RecordFeatureVisit("/studio", "first");
        sut.StagePrompt("second");
        sut.SetImage("data:image/png;base64,iVBORw0KGgo=", "image/png", "x.png");
        // Image swap should not clobber cross-page state
        Assert.Equal("first", sut.LastFinalPrompt);
        Assert.Equal("second", sut.StagedPrompt);
    }

    [Fact]
    public void StagePrompt_ignores_blank_input_so_a_failed_run_cannot_wipe_a_good_seed()
    {
        var sut = new ImageSessionService();
        sut.StagePrompt("good seed");
        sut.StagePrompt("   ");
        sut.StagePrompt("");
        sut.StagePrompt(null);
        Assert.Equal("good seed", sut.StagedPrompt);
    }

    [Fact]
    public void TakeStagedPrompt_returns_and_clears()
    {
        var sut = new ImageSessionService();
        sut.StagePrompt("only once");
        Assert.Equal("only once", sut.TakeStagedPrompt());
        Assert.Null(sut.TakeStagedPrompt()); // second call is null — consumed
    }
}