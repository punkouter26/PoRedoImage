using PoRedoImage.Client.Services;

namespace PoRedoImage.Tests.Unit.Features;

/// <summary>
/// Covers the karaoke timing model. Pure arithmetic over a string — no browser, no audio element —
/// which is the whole reason the apportionment lives in C# rather than in roastStage.js.
/// </summary>
public class RoastScriptTests
{
    private const string Lyrics = """
        [Verse]
        Stepped in the frame with a sharp kind of grin,
        Short bar.

        [Chorus]
        That's the shot, that's the one you kept.
        """;

    [Fact]
    public void Parse_apportions_the_track_across_sung_bars_and_leaves_markers_untimed()
    {
        var lines = RoastScript.Parse(Lyrics);

        // Blank lines are dropped; markers survive because they anchor the scroll.
        Assert.Equal(5, lines.Count);
        Assert.Equal(["[Verse]", "[Chorus]"], lines.Where(l => l.IsSection).Select(l => l.Text));

        var sung = lines.Where(l => !l.IsSection).ToList();

        // A marker is a place in the script, not a moment in the track: zero duration, sitting on
        // the boundary of the bar that follows it.
        foreach (var marker in lines.Where(l => l.IsSection))
        {
            Assert.Equal(marker.StartFraction, marker.EndFraction);
        }

        // Contiguous: every sung bar starts where the previous one ended, so no gap can leave the
        // highlight on nothing mid-verse.
        Assert.Equal(RoastScript.LeadInFraction, sung[0].StartFraction, precision: 6);
        for (var i = 1; i < sung.Count; i++)
        {
            Assert.Equal(sung[i - 1].EndFraction, sung[i].StartFraction, precision: 6);
        }
        Assert.Equal(1.0 - RoastScript.TailFraction, sung[^1].EndFraction, precision: 6);

        // Longer bars get proportionally longer — that is the entire estimate.
        Assert.True(sung[0].EndFraction - sung[0].StartFraction > sung[1].EndFraction - sung[1].StartFraction);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\n  ")]
    [InlineData("[Verse]\n[Chorus]")] // markers only — nothing to sing, so nothing to time
    public void Parse_returns_empty_when_there_is_nothing_to_perform(string? lyrics)
    {
        // An empty list is the caller's signal to render the plain lyric block instead of a karaoke
        // view that could never advance.
        Assert.Empty(RoastScript.Parse(lyrics));
    }
}
