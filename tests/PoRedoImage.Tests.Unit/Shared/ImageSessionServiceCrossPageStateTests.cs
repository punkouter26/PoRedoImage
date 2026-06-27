using PoRedoImage.Client.Shared;

namespace PoRedoImage.Tests.Unit.Shared;

/// <summary>
/// Locks the cross-page state contract of <see cref="ImageSessionService"/>:
/// <c>LastVisitedFeatureRoute</c> + <c>LastFinalPrompt</c> + <c>OnChange</c> wiring.
/// </summary>
public sealed class ImageSessionServiceCrossPageStateTests
{
    [Fact]
    public void Defaults_are_null_until_recorded()
    {
        var sut = new ImageSessionService();
        Assert.Null(sut.LastVisitedFeatureRoute);
        Assert.Null(sut.LastFinalPrompt);
    }

    [Fact]
    public void RecordFeatureVisit_sets_route_and_prompt()
    {
        var sut = new ImageSessionService();
        sut.RecordFeatureVisit("/bulk-generate", "studio noir, 16:9");
        Assert.Equal("/bulk-generate", sut.LastVisitedFeatureRoute);
        Assert.Equal("studio noir, 16:9", sut.LastFinalPrompt);
    }

    [Fact]
    public void RecordFeatureVisit_keeps_previous_prompt_when_null()
    {
        var sut = new ImageSessionService();
        sut.RecordFeatureVisit("/studio", "first prompt");
        sut.RecordFeatureVisit("/meme-generation"); // no prompt supplied
        Assert.Equal("/meme-generation", sut.LastVisitedFeatureRoute);
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
    public void Clear_resets_route_and_prompt()
    {
        var sut = new ImageSessionService();
        sut.RecordFeatureVisit("/bulk-generate", "lots of cats");
        sut.Clear();
        Assert.Null(sut.LastVisitedFeatureRoute);
        Assert.Null(sut.LastFinalPrompt);
    }

    [Fact]
    public void SetImage_does_not_touch_route_or_prompt()
    {
        var sut = new ImageSessionService();
        sut.RecordFeatureVisit("/studio", "first");
        sut.SetImage("data:image/png;base64,iVBORw0KGgo=", "image/png", "x.png");
        // Image swap should not clobber cross-page state
        Assert.Equal("/studio", sut.LastVisitedFeatureRoute);
        Assert.Equal("first", sut.LastFinalPrompt);
    }
}