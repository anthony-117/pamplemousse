using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Hotels;

namespace Pamplemousse.Domain.Tests.Hotels;

public class HotelTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 9, 0, 0, TimeSpan.Zero);
    private static readonly HotelPrice Price = new(500, 125, "USD", new DateOnly(2027, 4, 10), new DateOnly(2027, 4, 14), 2, Now);

    private static Hotel Stored() => new()
    {
        Id = "bk:1",
        Name = "Old name",
        Location = new GeoPoint(41.9, 12.5),
        StayType = "Hotels",
        Address = "Via Roma 1",
        FreeCancellation = true,
        Facilities = ["Free WiFi"],
        BookingPageUrl = "https://www.booking.com/hotel/it/one.html",
        RawDetails = "{}",
        DetailsUpdatedAt = Now.AddDays(-2),
        LastPrice = Price,
        RawSearch = "{\"old\":1}",
    };

    [Fact]
    public void A_new_search_keeps_the_stored_details()
    {
        var fresh = new Hotel { Id = "bk:1", Name = "New name", Location = new GeoPoint(41.91, 12.5), ReviewScore = 8.6, RawSearch = "{}", SearchUpdatedAt = Now };

        var merged = Stored().MergeWith(fresh);

        Assert.Equal(("New name", 8.6, "{}"), (merged.Name, merged.ReviewScore, merged.RawSearch));
        Assert.Equal(("Hotels", "Via Roma 1", true), (merged.StayType, merged.Address, merged.FreeCancellation));
        Assert.Equal(["Free WiFi"], merged.Facilities);
        Assert.Equal(Price, merged.LastPrice);
    }

    [Fact]
    public void New_details_keep_the_stored_price_and_search_entry()
    {
        var details = new Hotel { Id = "bk:1", Name = "Old name", Location = new GeoPoint(41.9, 12.5), FreeCancellation = false, Facilities = ["Pool"], DetailsUpdatedAt = Now };

        var merged = Stored().MergeWith(details);

        Assert.Equal((false, Now), (merged.FreeCancellation, merged.DetailsUpdatedAt));
        Assert.Equal(["Pool"], merged.Facilities);
        Assert.Equal((Price, "{\"old\":1}"), (merged.LastPrice, merged.RawSearch));
    }

    [Fact]
    public void An_empty_name_never_replaces_a_stored_one()
    {
        var merged = Stored().MergeWith(new Hotel { Id = "bk:1", Name = " ", Location = new GeoPoint(41.9, 12.5) });

        Assert.Equal("Old name", merged.Name);
    }

    [Fact]
    public void Details_are_stale_after_a_week_or_when_missing()
    {
        Assert.False(Stored().DetailsAreStale(Now));
        Assert.True(Stored().DetailsAreStale(Now.AddDays(6)));
        Assert.True((Stored() with { DetailsUpdatedAt = null }).DetailsAreStale(Now));
    }

    [Fact]
    public void Distance_between_two_points_is_in_kilometres()
    {
        var rome = new GeoPoint(41.8967, 12.4822);
        var paris = new GeoPoint(48.8566, 2.3522);

        Assert.InRange(rome.DistanceKmTo(paris), 1100, 1110);
    }
}
