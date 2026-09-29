using PoRedoImage.Client.Services;
using Xunit;

namespace PoRedoImage.Tests.Unit.Services;

public sealed class JobTrayServiceTests
{
    /// <summary>
    /// The persisted video handles are what recover a paid render after a reload, so a malformed
    /// line must cost only itself — never the whole list.
    /// </summary>
    [Fact]
    public void ParseHandles_keeps_valid_lines_and_skips_malformed_ones()
    {
        const string raw = "1790000000|models/veo/operations/abc\n"
            + "garbage\n"
            + "notanumber|operations/x\n"
            + "1790000100|\n"
            + " 1790000200|operations/def \n";

        var handles = JobTrayService.ParseHandles(raw).ToList();

        Assert.Equal(
            [
                ("models/veo/operations/abc", DateTimeOffset.FromUnixTimeSeconds(1790000000)),
                ("operations/def", DateTimeOffset.FromUnixTimeSeconds(1790000200)),
            ],
            handles);
        Assert.Empty(JobTrayService.ParseHandles(null));
    }
}
