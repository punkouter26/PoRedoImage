using PoRedoImage.Infrastructure.Services;
using Xunit;

namespace PoRedoImage.Tests.Unit.Services;

/// <summary>
/// Veo exposes no audio parameter, so the prompt is the only lever — and with no audio direction it
/// commonly returns a silent clip while still billing the "with audio" rate. These cases pin the two
/// halves of the rule: add the directive when the caller said nothing about sound, and never touch a
/// prompt that already took a position — including one that asked for silence.
/// </summary>
public sealed class VeoAudioDirectionTests
{
    [Theory]
    // Nothing about sound → the directive is appended.
    [InlineData("a man reads a menu", true)]
    [InlineData("slow dolly across the table", true)]
    [InlineData("", true)]
    // Already asks for sound → left exactly as written.
    [InlineData("he laughs, cutlery clinking", false)]
    [InlineData("add upbeat music", false)]
    [InlineData("a narrator describes the scene", false)]
    [InlineData("she speaks to camera", false)]
    // Explicitly asks for NO sound → must not be contradicted two sentences later.
    [InlineData("a silent film pastiche", false)]
    [InlineData("keep it quiet, no sound", false)]
    public void Audio_directive_is_added_only_when_the_prompt_is_silent_on_sound(
        string prompt, bool expectDirective)
    {
        var result = VeoVideoGenerationService.WithAudioDirection(prompt);

        Assert.Equal(expectDirective, result.EndsWith(VeoVideoGenerationService.AudioDirective, StringComparison.Ordinal));

        // The caller's own words survive verbatim either way — the directive only ever appends.
        Assert.StartsWith(prompt.TrimEnd(), result, StringComparison.Ordinal);
    }
}
