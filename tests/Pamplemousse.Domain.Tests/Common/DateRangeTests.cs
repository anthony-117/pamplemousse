using System.Globalization;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Exceptions;

namespace Pamplemousse.Domain.Tests.Common;

public class DateRangeTests
{
    private static readonly DateOnly Today = new(2026, 10, 27);

    [Fact]
    public void Defaults_to_a_5_day_trip_starting_in_30_days()
    {
        var dates = DateRange.ForTrip(null, null, Today);

        Assert.Equal(new DateOnly(2026, 11, 26), dates.From);
        Assert.Equal(new DateOnly(2026, 11, 30), dates.To);
    }

    [Fact]
    public void Only_start_given_adds_4_days()
    {
        var dates = DateRange.ForTrip(new DateOnly(2027, 4, 10), null, Today);

        Assert.Equal(new DateOnly(2027, 4, 14), dates.To);
    }

    [Theory]
    [InlineData("2027-04-10", "2027-04-09")] // ends before it starts
    [InlineData("2027-04-10", "2027-05-11")] // 31 days
    [InlineData("2026-10-01", "2026-10-03")] // in the past
    public void Invalid_ranges_are_rejected_and_name_the_field(string from, string to)
    {
        var error = Assert.Throws<DomainException>(() =>
            DateRange.ForTrip(DateOnly.Parse(from, CultureInfo.InvariantCulture), DateOnly.Parse(to, CultureInfo.InvariantCulture), Today));

        Assert.Equal("to", error.Field);
    }

    [Fact]
    public void A_30_day_trip_is_allowed()
    {
        var dates = DateRange.ForTrip(new DateOnly(2027, 4, 1), new DateOnly(2027, 5, 1), Today);

        Assert.Equal(30, dates.To.DayNumber - dates.From.DayNumber);
    }
}
