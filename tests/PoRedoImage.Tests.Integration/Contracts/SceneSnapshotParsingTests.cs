using PoRedoImage.Application.Features.RapRoast;
using PoRedoImage.Domain.Interfaces;

namespace PoRedoImage.Tests.Integration.Contracts;

/// <summary>
/// Contract tests for the structured scene read — the JSON shape the vision model must produce and
/// the tolerance applied when it doesn't quite.
/// </summary>
/// <remarks>
/// These live in the Integration tier by the convention <c>TestCountCeilingTests</c> documents:
/// contract/DTO tests sit here so the Unit tier stays reserved for pure behavioural logic. They are
/// still I/O-free — parsing a string is the entire exercise.
/// <para>
/// Worth testing precisely because the failure mode is silent. A model that returns a bare string
/// where an array was specified, or wraps its answer in a fence, would otherwise produce an empty
/// snapshot and the roast would quietly regress to generic bars with nothing indicating why.
/// </para>
/// </remarks>
public class SceneSnapshotParsingTests
{
    [Fact]
    public void Parses_a_well_formed_snapshot()
    {
        const string json = """
            {"outfit":["oversized navy hoodie","white trainers, scuffed"],
             "pose":"leaning on the counter, one hand around a paper cup",
             "expression":"mid-sentence, eyebrows up",
             "setting":"fast food restaurant, backlit menu board behind",
             "props":["tray of fries, half eaten"],
             "text_in_image":["2 FOR $5","OPEN 24 HOURS"],
             "most_incongruous_detail":"the third menu panel is burnt out"}
            """;

        var snapshot = SceneSnapshot.Parse(json);

        Assert.Equal(2, snapshot.Outfit.Count);
        Assert.Contains("oversized navy hoodie", snapshot.Outfit);
        Assert.Equal("mid-sentence, eyebrows up", snapshot.Expression);
        Assert.Contains("2 FOR $5", snapshot.TextInImage);
        Assert.Contains("burnt out", snapshot.MostIncongruousDetail);
        Assert.True(snapshot.HasSubstance);
    }

    [Theory]
    // Fenced output, which models emit despite being told not to.
    [InlineData("```json\n{\"pose\":\"standing\"}\n```")]
    // Commentary wrapped around the object.
    [InlineData("Here is the JSON you asked for: {\"pose\":\"standing\"} — hope that helps!")]
    public void Tolerates_fences_and_surrounding_commentary(string content)
    {
        var snapshot = SceneSnapshot.Parse(content);

        Assert.Equal("standing", snapshot.Pose);
    }

    [Fact]
    public void Accepts_a_bare_string_where_an_array_was_specified()
    {
        // Models routinely collapse a single-element array to a string. Rejecting it would discard
        // a perfectly good answer.
        var snapshot = SceneSnapshot.Parse("""{"outfit":"a single grey coat","props":[]}""");

        Assert.Equal(["a single grey coat"], snapshot.Outfit);
        Assert.Empty(snapshot.Props);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("")]
    [InlineData("{ this is broken")]
    public void Malformed_output_degrades_to_empty_rather_than_throwing(string content)
    {
        // Losing the structure must cost the detail, never the whole roast.
        var snapshot = SceneSnapshot.Parse(content);

        Assert.False(snapshot.HasSubstance);
        Assert.Empty(snapshot.Outfit);
    }

    [Fact]
    public void Prose_rendering_omits_empty_slots()
    {
        var prose = SceneSnapshot.Parse("""{"pose":"seated","outfit":[],"setting":""}""").ToProse();

        Assert.Contains("Pose: seated", prose, StringComparison.Ordinal);
        // An empty slot rendered as "Wearing: ." would read as a claim that nothing was worn.
        Assert.DoesNotContain("Wearing", prose, StringComparison.Ordinal);
        Assert.DoesNotContain("Setting", prose, StringComparison.Ordinal);
    }

    [Fact]
    public void Scene_details_prompt_block_omits_absent_sections()
    {
        var details = new SceneDetails(
            TextLines: ["OPEN 24 HOURS"],
            RegionCaptions: [],
            Objects: ["tray", "cup"],
            PeopleCount: 1,
            ElapsedMs: 5);

        var block = details.ToPromptBlock();

        Assert.Contains("OPEN 24 HOURS", block, StringComparison.Ordinal);
        Assert.Contains("tray, cup", block, StringComparison.Ordinal);
        // "Region captions: (none)" would read to the model as a meaningful absence.
        Assert.DoesNotContain("Region captions", block, StringComparison.Ordinal);
        Assert.True(details.HasAny);
        Assert.False(SceneDetails.Empty.HasAny);
    }
}
