using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Flights;

namespace Pamplemousse.Domain.Tests.Flights;

public class FlightSearchTests
{
    private static readonly DateRange Dates = DateRange.ForTrip(new DateOnly(2027, 4, 10), new DateOnly(2027, 4, 14), new DateOnly(2026, 11, 2));

    [Fact]
    public void Currency_defaults_to_usd()
    {
        Assert.Equal("USD", FlightSearch.Create("PAR", "ROM", Dates, TripType.RoundTrip, directOnly: false, currency: null).Currency);
    }

    [Fact]
    public void Currency_is_normalised()
    {
        Assert.Equal("EUR", FlightSearch.Create("PAR", "ROM", Dates, TripType.RoundTrip, directOnly: false, currency: " eur ").Currency);
    }

    [Theory]
    [InlineData("EURO")]
    [InlineData("E1R")]
    [InlineData("€€€")]
    public void A_badly_formed_currency_is_rejected_on_its_field(string currency)
    {
        var error = Assert.Throws<DomainException>(() => FlightSearch.Create("PAR", "ROM", Dates, TripType.RoundTrip, directOnly: false, currency));

        Assert.Equal("currency", error.Field);
    }

    [Theory]
    [InlineData("BEY", "BEY")]
    [InlineData("bey", "BEY")] // the same flight city, whatever the case
    public void The_same_flight_city_twice_is_rejected_on_the_origin(string origin, string destination)
    {
        var error = Assert.Throws<DomainException>(() => FlightSearch.Create(origin, destination, Dates, TripType.RoundTrip, directOnly: false, currency: null));

        Assert.Equal("origin", error.Field);
    }

    [Fact]
    public void It_keeps_what_was_asked()
    {
        var search = FlightSearch.Create("PAR", "ROM", Dates, TripType.OneWay, directOnly: true, currency: "USD");

        Assert.Equal(("PAR", "ROM", TripType.OneWay, true), (search.OriginCode, search.DestinationCode, search.TripType, search.DirectOnly));
    }
}
