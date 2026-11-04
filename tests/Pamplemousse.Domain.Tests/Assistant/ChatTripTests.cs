using Pamplemousse.Domain.Assistant;
using Pamplemousse.Domain.Exceptions;

namespace Pamplemousse.Domain.Tests.Assistant;

public class ChatTripTests
{
    private static readonly DateOnly Today = new(2026, 11, 4);

    [Fact]
    public void Without_values_uses_the_default_trip_passport_currency_and_adults()
    {
        var trip = ChatTrip.Create(null, null, null, null, null, null, null, Today);

        Assert.Null(trip.DestinationId);
        Assert.Null(trip.OriginId);
        Assert.Equal((Today.AddDays(30), Today.AddDays(34)), (trip.Dates.From, trip.Dates.To));
        Assert.Equal((2, "LB", "USD"), (trip.Adults, trip.Passport, trip.Currency));
    }

    [Fact]
    public void Keeps_the_given_values_trimmed_and_upper_cased()
    {
        var trip = ChatTrip.Create(" 3169070 ", new DateOnly(2027, 4, 10), new DateOnly(2027, 4, 15), 3, "fr", "eur", "  ", Today);

        Assert.Equal(("3169070", 3, "FR", "EUR"), (trip.DestinationId, trip.Adults, trip.Passport, trip.Currency));
        Assert.Null(trip.OriginId);
        Assert.Equal(new DateOnly(2027, 4, 15), trip.Dates.To);
    }

    [Theory]
    [InlineData(0, null, null, "context.adults")]
    [InlineData(9, null, null, "context.adults")]
    [InlineData(null, "LBN", null, "context.passport")]
    [InlineData(null, null, "US", "context.currency")]
    [InlineData(null, null, "U5D", "context.currency")]
    public void Rejects_bad_values_naming_the_field(int? adults, string? passport, string? currency, string field)
    {
        var ex = Assert.Throws<DomainException>(() => ChatTrip.Create(null, null, null, adults, passport, currency, null, Today));

        Assert.Equal(field, ex.Field);
    }

    [Fact]
    public void Rejects_dates_the_other_screens_reject()
    {
        Assert.Throws<DomainException>(() => ChatTrip.Create(null, new DateOnly(2027, 4, 15), new DateOnly(2027, 4, 10), null, null, null, null, Today));
    }
}
