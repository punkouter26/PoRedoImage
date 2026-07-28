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
        // "Cat" is exactly 3 characters, so it clears the primary length filter (>= 3) directly
        // and never reaches the fallback path. This is a primary-path case, not a fallback case.
        { "Cat.", ["cat"] },
        // Empty and whitespace-only input must not produce a blank tag.
        { "", [] },
        { "   ", [] },
        // Every token is shorter than 3 characters, so the primary path yields nothing and the
        // fallback path returns the trimmed, lowercased description as a single tag.
        { "hi ok no", ["hi ok no"] },
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
