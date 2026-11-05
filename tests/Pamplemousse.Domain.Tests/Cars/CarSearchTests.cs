using Pamplemousse.Domain.Cars;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Exceptions;

namespace Pamplemousse.Domain.Tests.Cars;

public class CarSearchTests
{
    private static readonly DateRange Dates = DateRange.ForTrip(new DateOnly(2027, 4, 10), new DateOnly(2027, 4, 14), new DateOnly(2026, 11, 3));
    private static readonly PickUpPlace Centre = PickUpPlace.Centre(new GeoPoint(41.89, 12.51));

    [Fact]
    public void Defaults_to_a_30_year_old_driver_and_usd()
    {
        var search = CarSearch.Create("3169070", Centre, Dates, driverAge: null, currency: null);

        Assert.Equal((30, "USD", 4), (search.DriverAge, search.Currency, search.Days));
        Assert.Null(search.DriverNote);
    }

    [Fact]
    public void The_key_names_everything_that_changes_the_cars()
    {
        var airport = PickUpPlace.Airport("fco", "Fiumicino Airport", new GeoPoint(41.79, 12.25));

        var search = CarSearch.Create("3169070", airport, Dates, driverAge: 45, currency: " eur ");

        Assert.Equal("3169070:FCO:20270410:20270414:45:EUR", search.Key);
    }

    [Fact]
    public void Pick_up_and_return_are_at_ten()
    {
        Assert.Equal(new TimeOnly(10, 0), CarSearch.PickUpTime);
    }

    [Theory]
    [InlineData(17)]
    [InlineData(100)]
    public void A_driver_age_outside_18_to_99_is_rejected(int age)
    {
        var ex = Assert.Throws<DomainException>(() => CarSearch.Create("3169070", Centre, Dates, age, null));

        Assert.Equal("driverAge", ex.Field);
    }

    [Theory]
    [InlineData("EURO")]
    [InlineData("E1R")]
    public void A_bad_currency_is_rejected(string currency)
    {
        var ex = Assert.Throws<DomainException>(() => CarSearch.Create("3169070", Centre, Dates, null, currency));

        Assert.Equal("currency", ex.Field);
    }

    [Theory]
    [InlineData(18, "young driver")]
    [InlineData(24, "young driver")]
    [InlineData(71, "senior driver")]
    public void Young_and_senior_drivers_get_a_note(int age, string note)
    {
        var search = CarSearch.Create("3169070", Centre, Dates, age, null);

        Assert.Contains(note, search.DriverNote, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(25)]
    [InlineData(70)]
    public void Drivers_from_25_to_70_get_no_note(int age)
    {
        Assert.Null(CarSearch.Create("3169070", Centre, Dates, age, null).DriverNote);
    }

    [Fact]
    public void An_airport_place_is_named_by_its_upper_case_code()
    {
        var airport = PickUpPlace.Airport("cia", "Ciampino Airport", new GeoPoint(41.8, 12.59));

        Assert.Equal(("CIA", "CIA", PickUpKind.Airport), (airport.Id, airport.Iata, airport.Kind));
        Assert.Equal((PickUpPlace.CentreId, PickUpKind.Centre, (string?)null), (Centre.Id, Centre.Kind, Centre.Iata));
    }
}
