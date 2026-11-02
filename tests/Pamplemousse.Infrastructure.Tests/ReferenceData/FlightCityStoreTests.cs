using Pamplemousse.Domain.Common;
using Pamplemousse.Infrastructure.ReferenceData;

namespace Pamplemousse.Infrastructure.Tests.ReferenceData;

/// <summary>Runs against the real embedded lists, so a badly rebuilt file fails here.</summary>
public class FlightCityStoreTests
{
    private static readonly FlightCityStore Store = new();

    [Theory]
    [InlineData(41.89, 12.51, "ROM")]    // Rome, not one of the US cities named Rome
    [InlineData(33.89, 35.50, "BEY")]    // Beirut
    [InlineData(34.12, 35.65, "BEY")]    // Byblos has no airport
    [InlineData(48.80, 2.13, "PAR")]     // Versailles
    [InlineData(46.02, 7.75, "SIR")]     // Zermatt
    [InlineData(-54.80, -68.30, "USH")]  // Ushuaia
    public void Finds_the_nearest_city_with_an_airport(double lat, double lng, string code)
    {
        Assert.Equal(code, Store.FindNearest(new GeoPoint(lat, lng))?.City.Code);
    }

    [Fact]
    public void Reports_the_distance()
    {
        var match = Store.FindNearest(new GeoPoint(34.12, 35.65));

        Assert.NotNull(match);
        Assert.InRange(match.DistanceKm, 20, 40);
    }

    [Fact]
    public void Nothing_in_range_returns_null()
    {
        Assert.Null(Store.FindNearest(new GeoPoint(-48.87, -123.39))); // the middle of the South Pacific
    }

    [Fact]
    public void Railway_stations_do_not_count_as_airports()
    {
        // Disneyland Paris (XED) is in the source's list as a flightable railway station.
        Assert.NotEqual("XED", Store.FindNearest(new GeoPoint(48.87, 2.78))?.City.Code, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("AF", "Air France")]
    [InlineData("ME", "Middle East Airlines")]
    [InlineData("??", "??")] // unknown: the code itself
    public void Names_airlines(string code, string name)
    {
        Assert.Equal(name, Store.AirlineName(code));
    }
}
