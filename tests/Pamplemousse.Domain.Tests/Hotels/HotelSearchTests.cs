using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Hotels;

namespace Pamplemousse.Domain.Tests.Hotels;

public class HotelSearchTests
{
    private static readonly DateOnly Today = new(2026, 11, 3);
    private static readonly DateRange Dates = DateRange.ForTrip(new DateOnly(2027, 4, 10), new DateOnly(2027, 4, 14), Today);
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Defaults_to_two_adults_and_usd()
    {
        var search = HotelSearch.Create("3169070", Dates, adults: null, currency: null);

        Assert.Equal((2, "USD", 4), (search.Adults, search.Currency, search.Nights));
    }

    [Fact]
    public void The_key_names_everything_that_changes_the_prices()
    {
        var search = HotelSearch.Create("3169070", Dates, adults: 3, currency: " eur ");

        Assert.Equal("3169070:20270410:20270414:3:EUR", search.Key);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(-1)]
    public void Adults_outside_one_to_eight_are_rejected_on_their_field(int adults)
    {
        var error = Assert.Throws<DomainException>(() => HotelSearch.Create("3169070", Dates, adults, currency: null));

        Assert.Equal("adults", error.Field);
    }

    [Theory]
    [InlineData("EURO")]
    [InlineData("E1R")]
    public void A_badly_formed_currency_is_rejected_on_its_field(string currency)
    {
        var error = Assert.Throws<DomainException>(() => HotelSearch.Create("3169070", Dates, adults: null, currency));

        Assert.Equal("currency", error.Field);
    }

    [Fact]
    public void Checking_out_on_the_check_in_day_is_rejected()
    {
        var sameDay = DateRange.ForTrip(new DateOnly(2027, 4, 10), new DateOnly(2027, 4, 10), Today);

        var error = Assert.Throws<DomainException>(() => HotelSearch.Create("3169070", sameDay, adults: null, currency: null));

        Assert.Equal("to", error.Field);
    }

    [Fact]
    public void A_price_is_the_total_and_per_night_for_the_stay()
    {
        var price = HotelPrice.ForStay(812.401m, HotelSearch.Create("3169070", Dates, adults: null, currency: null), Now);

        Assert.Equal((812.40m, 203.10m, "USD", 2), (price?.Total, price?.PerNight, price?.Currency, price?.Adults));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_missing_or_non_positive_price_is_no_price(int? total)
    {
        Assert.Null(HotelPrice.ForStay(total, HotelSearch.Create("3169070", Dates, adults: null, currency: null), Now));
    }
}
