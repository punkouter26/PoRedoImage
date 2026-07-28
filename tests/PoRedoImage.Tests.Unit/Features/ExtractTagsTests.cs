using PoRedoImage.Client.Pages;
using Xunit;

namespace PoRedoImage.Tests.Unit.Features;

/// <summary>
/// Covers <c>FeaturePageBase.ExtractTags</c> — the heuristic that turns a browser vision model's
/// free-text description into the coarse tag list the server's meme branch captions from. Made
/// reachable via the existing <c>InternalsVisibleTo</c> grant in
/// PoRedoImage.Client.csproj (no new grant added).
/// </summary>
public class ExtractTagsTests
{
    public static TheoryData<string, string[]> Cases() => new()
    {
        // Terse captions are the realistic case: common 3-letter nouns (here, "cat") must survive
        // the length filter, not just longer words.
        { "A cat sitting on a couch.", ["cat", "sitting", "couch"] },
        // A caption with nothing 3+ letters long except the fallback-worthy word itself still
        // yields a real tag ("cat"), whether via the filter or the fallback path.
        { "Cat.", ["cat"] },
        // Empty and whitespace-only input must not produce a blank tag.
        { "", [] },
        { "   ", [] },
        // Duplicates collapse (case-insensitively) and the list is capped at 10, preserving the
        // order words were first encountered. "kilo" and "lima" are the 11th/12th distinct words
        // and must be dropped; the repeated "alpha"/"bravo" must not appear twice.
        {
            "alpha bravo charlie delta echo foxtrot golf hotel india juliet kilo lima alpha bravo",
            ["alpha", "bravo", "charlie", "delta", "echo", "foxtrot", "golf", "hotel", "india", "juliet"]
        },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void ExtractTags_matches_expected_output(string description, string[] expected)
    {
        var tags = FeaturePageBase.ExtractTags(description);

        Assert.Equal(expected, tags);
    }
}
