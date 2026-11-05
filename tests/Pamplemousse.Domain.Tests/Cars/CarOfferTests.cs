using Pamplemousse.Domain.Cars;
using Pamplemousse.Domain.Common;

namespace Pamplemousse.Domain.Tests.Cars;

public class CarOfferTests
{
    [Fact]
    public void The_price_is_the_normal_price_when_the_source_gives_one()
    {
        var price = CarPrice.For(76.76m, 68.32m, 4, "USD", payNow: true)!;

        Assert.Equal((76.76m, 19.19m, "USD", true), (price.Total, price.PerDay, price.Currency, price.PayNow));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    public void Without_a_normal_price_the_shown_price_is_used(double? normal)
    {
        var price = CarPrice.For((decimal?)normal, 100m, 3, "EUR", payNow: false)!;

        Assert.Equal((100m, 33.33m), (price.Total, price.PerDay));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    public void A_missing_or_zero_price_gives_no_price(double? price)
    {
        Assert.Null(CarPrice.For(null, (decimal?)price, 4, "USD", payNow: true));
    }

    [Fact]
    public void Distinct_keeps_the_cheapest_of_the_same_model_company_and_desk_in_source_order()
    {
        CarOffer[] offers =
        [
            CarTestData.Car(1, 300, model: "Fiat 500"),
            CarTestData.Car(2, 400, model: "Fiat Panda"),
            CarTestData.Car(3, 250, model: "FIAT 500"),
            CarTestData.Car(4, 200, model: "Fiat 500", company: "Hertz"),
            CarTestData.Car(5, 100, model: "Fiat 500", desk: "Airport desk"),
        ];

        var kept = CarOffer.Distinct(offers);

        Assert.Equal(["bk:2", "bk:3", "bk:4", "bk:5"], kept.Select(o => o.Id), StringComparer.Ordinal);
    }
}

internal static class CarTestData
{
    public static CarOffer Car(
        int rank,
        decimal total,
        string model = "Fiat 500",
        string company = "Avis",
        string desk = "Via Roma",
        string size = "Mini",
        Gearbox? gearbox = Gearbox.Manual,
        int? seats = 4,
        int? bags = 1,
        double? rating = 8.0,
        PickUpType type = PickUpType.Downtown,
        bool? freeCancellation = true) => new(
        $"bk:{rank}",
        model,
        null,
        size,
        null,
        gearbox,
        seats,
        4,
        bags,
        true,
        null,
        true,
        null,
        new RentalCompany(company, null, rating, 10),
        new PickUpPoint(desk, type, null, new GeoPoint(41.9, 12.5), 1),
        CarPrice.For(total, null, 4, "USD", payNow: true)!,
        freeCancellation,
        null,
        null,
        Sponsored: false,
        rank,
        null);
}
