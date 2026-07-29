using PoRedoImage.Infrastructure.Repositories;
using Xunit;

namespace PoRedoImage.Tests.Unit.Features;

/// <summary>
/// The <c>ClientVitals</c> table's read path depends entirely on row keys sorting newest-first,
/// because Table Storage cannot reverse a query's order. These tests pin that guarantee — if the
/// inversion breaks, "most recent 500 samples" silently becomes "oldest 500 samples", which the
/// dashboard would render without complaint.
/// </summary>
public class ClientVitalsKeysTests
{
    [Fact]
    public void PartitionKey_is_the_utc_day_regardless_of_source_offset()
    {
        // 01:30 on the 5th in UTC+13 is still the 4th in UTC — the partition must follow UTC,
        // or a user in New Zealand writes into tomorrow's partition and falls out of the window.
        var local = new DateTimeOffset(2026, 7, 5, 1, 30, 0, TimeSpan.FromHours(13));

        Assert.Equal("2026-07-04", ClientVitalsKeys.PartitionKeyFor(local));
    }

    [Fact]
    public void RowKeys_sort_newest_first_as_ordinal_strings()
    {
        var older = ClientVitalsKeys.RowKeyFor(new DateTimeOffset(2026, 7, 4, 10, 0, 0, TimeSpan.Zero), "aaaaaaaa");
        var newer = ClientVitalsKeys.RowKeyFor(new DateTimeOffset(2026, 7, 4, 11, 0, 0, TimeSpan.Zero), "aaaaaaaa");

        // Ordinal comparison is exactly what Table Storage applies to RowKey.
        Assert.True(string.CompareOrdinal(newer, older) < 0,
            "the newer sample's row key must sort before the older one");
    }

    [Fact]
    public void RowKey_ticks_segment_is_fixed_width_so_ordinal_sorting_matches_numeric_order()
    {
        // Without zero-padding to a fixed width, "9…" would sort after "10…" and the ordering
        // guarantee would hold only by accident for timestamps of equal magnitude.
        var key = ClientVitalsKeys.RowKeyFor(DateTimeOffset.UnixEpoch, "abcdef01");
        var ticksSegment = key[..key.IndexOf('-')];

        Assert.Equal(19, ticksSegment.Length);
        Assert.All(ticksSegment, c => Assert.True(char.IsAsciiDigit(c)));
    }

    [Fact]
    public void TryParseTimestamp_round_trips_the_encoded_instant()
    {
        var original = new DateTimeOffset(2026, 7, 28, 20, 41, 3, TimeSpan.Zero);
        var key = ClientVitalsKeys.RowKeyFor(original, "deadbeef");

        Assert.True(ClientVitalsKeys.TryParseTimestamp(key, out var parsed));
        Assert.Equal(original.UtcTicks, parsed.UtcTicks);
    }

    [Fact]
    public void TryParseTimestamp_rejects_a_malformed_row_key()
    {
        // Guards the mapper's fallback: a hand-written or legacy row must not yield a bogus
        // timestamp that would place the sample at a nonsensical point on the chart.
        Assert.False(ClientVitalsKeys.TryParseTimestamp("not-a-key", out _));
    }
}
