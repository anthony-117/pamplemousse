using System.Globalization;
using Pamplemousse.Api.Features.Destinations;

namespace Pamplemousse.Api.Tests.Destinations;

public class ResolveDatesTests
{
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);
    }

    private static readonly FixedClock Clock = new();

    [Fact]
    public void Defaults_to_a_5_day_trip_starting_in_30_days()
    {
        Assert.Equal(new TripDates(new DateOnly(2026, 11, 26), new DateOnly(2026, 11, 30)), DestinationEndpoints.ResolveDates(null, null, Clock));
    }

    [Fact]
    public void Only_start_given_adds_4_days()
    {
        var start = new DateOnly(2027, 4, 10);

        Assert.Equal(new TripDates(start, new DateOnly(2027, 4, 14)), DestinationEndpoints.ResolveDates(start, null, Clock));
    }

    [Theory]
    [InlineData("2027-04-10", "2027-04-09")] // ends before it starts
    [InlineData("2027-04-10", "2027-05-11")] // 31 days
    [InlineData("2026-10-01", "2026-10-03")] // in the past
    public void Invalid_ranges_are_rejected(string from, string to)
    {
        Assert.Null(DestinationEndpoints.ResolveDates(DateOnly.Parse(from, CultureInfo.InvariantCulture), DateOnly.Parse(to, CultureInfo.InvariantCulture), Clock));
    }

    [Fact]
    public void A_30_day_trip_is_allowed()
    {
        Assert.NotNull(DestinationEndpoints.ResolveDates(new DateOnly(2027, 4, 1), new DateOnly(2027, 5, 1), Clock));
    }
}
