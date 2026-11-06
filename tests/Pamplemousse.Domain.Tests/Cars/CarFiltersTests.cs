using Pamplemousse.Domain.Cars;
using Pamplemousse.Domain.Exceptions;

namespace Pamplemousse.Domain.Tests.Cars;

public class CarFiltersTests
{
    private static readonly CarOffer[] Offers =
    [
        CarTestData.Car(1, 300, model: "Fiat 500", size: "Mini", rating: 7.0, bags: 1),
        CarTestData.Car(2, 500, model: "VW Golf", size: "Compact", gearbox: Gearbox.Automatic, seats: 5, bags: 3, rating: null, company: "Hertz", type: PickUpType.Terminal),
        CarTestData.Car(3, 200, model: "Kia Picanto", size: "Mini", rating: 9.0, seats: null, bags: null, freeCancellation: false, type: PickUpType.TrainStation),
        CarTestData.Car(4, 250, model: "Fiat Panda", size: "Mini", rating: 9.0, company: "Sixt"),
    ];

    private static IEnumerable<string> Ids(IEnumerable<CarOffer> offers) => offers.Select(o => o.Id);

    [Fact]
    public void Defaults_keep_every_car_in_the_source_order()
    {
        var filters = CarFilters.Create();

        Assert.Equal(["bk:1", "bk:2", "bk:3", "bk:4"], Ids(filters.Apply(Offers)), StringComparer.Ordinal);
        Assert.Equal((CarSort.Recommended, 1, false), (filters.Sort, filters.Page, filters.FreeCancellation));
    }

    [Fact]
    public void Price_sort_puts_the_cheapest_first()
    {
        Assert.Equal(["bk:3", "bk:4", "bk:1", "bk:2"], Ids(CarFilters.Create(sort: "PRICE").Apply(Offers)), StringComparer.Ordinal);
    }

    [Fact]
    public void Rating_sort_puts_unrated_last_and_the_cheaper_first_on_a_tie()
    {
        Assert.Equal(["bk:3", "bk:4", "bk:1", "bk:2"], Ids(CarFilters.Create(sort: "rating").Apply(Offers)), StringComparer.Ordinal);
    }

    [Fact]
    public void Each_filter_narrows_the_cars()
    {
        Assert.Equal(["bk:2"], Ids(CarFilters.Create(sizes: ["compact"]).Apply(Offers)), StringComparer.Ordinal);
        Assert.Equal(["bk:2"], Ids(CarFilters.Create(gearbox: "Automatic").Apply(Offers)), StringComparer.Ordinal);
        Assert.Equal(["bk:2", "bk:4"], Ids(CarFilters.Create(companies: ["hertz", "SIXT"]).Apply(Offers)), StringComparer.Ordinal);
        Assert.Equal(["bk:2", "bk:3"], Ids(CarFilters.Create(pickUpTypes: ["terminal", "trainStation"]).Apply(Offers)), StringComparer.Ordinal);
        Assert.Equal(["bk:1", "bk:3", "bk:4"], Ids(CarFilters.Create(maxPrice: 300).Apply(Offers)), StringComparer.Ordinal);
        Assert.Equal(["bk:1", "bk:2", "bk:4"], Ids(CarFilters.Create(freeCancellation: true).Apply(Offers)), StringComparer.Ordinal);
    }

    [Fact]
    public void Unknown_seats_or_bags_never_meet_a_minimum()
    {
        Assert.Equal(["bk:2"], Ids(CarFilters.Create(minSeats: 5).Apply(Offers)), StringComparer.Ordinal);
        Assert.Equal(["bk:1", "bk:2", "bk:4"], Ids(CarFilters.Create(minBags: 1).Apply(Offers)), StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("cheapest", null, null, null, null, null, null, "sort")]
    [InlineData("1", null, null, null, null, null, null, "sort")]
    [InlineData(null, "cvt", null, null, null, null, null, "gearbox")]
    [InlineData(null, "0", null, null, null, null, null, "gearbox")]
    [InlineData(null, null, 0, null, null, null, null, "minSeats")]
    [InlineData(null, null, null, 10, null, null, null, "minBags")]
    [InlineData(null, null, null, null, "boat", null, null, "pickUpType")]
    [InlineData(null, null, null, null, null, 0.0, null, "maxPrice")]
    [InlineData(null, null, null, null, null, null, 0, "page")]
    public void Bad_values_are_rejected_on_their_field(
        string? sort, string? gearbox, int? minSeats, int? minBags, string? pickUpType, double? maxPrice, int? page, string field)
    {
        var ex = Assert.Throws<DomainException>(() => CarFilters.Create(
            sort, null, gearbox, minSeats, minBags, null, pickUpType is null ? null : [pickUpType], (decimal?)maxPrice, null, page));

        Assert.Equal(field, ex.Field);
    }

    [Fact]
    public void Pages_hold_twenty_cars()
    {
        var many = Enumerable.Range(1, 45).Select(i => CarTestData.Car(i, 100 + i, model: $"Car {i}")).ToList();

        var third = CarFilters.Create(page: 3).PageOf(many);

        Assert.Equal(["bk:41", "bk:42", "bk:43", "bk:44", "bk:45"], Ids(third), StringComparer.Ordinal);
        Assert.Empty(CarFilters.Create(page: 4).PageOf(many));
    }

    [Fact]
    public void Filter_options_count_every_car()
    {
        var options = CarFilterOptions.Of(Offers);

        Assert.Contains(options.Sizes, o => string.Equals(o.Value, "Mini", StringComparison.Ordinal) && o.Count == 3);
        Assert.Contains(options.Companies, o => string.Equals(o.Value, "Avis", StringComparison.Ordinal) && o.Count == 2);
        Assert.Contains(options.PickUpTypes, o => string.Equals(o.Value, "Downtown", StringComparison.Ordinal) && o.Count == 2);
    }
}
