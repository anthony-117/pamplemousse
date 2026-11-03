using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Hotels;

namespace Pamplemousse.Domain.Tests.Hotels;

public class HotelFiltersTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 9, 0, 0, TimeSpan.Zero);

    private static HotelOffer Offer(int rank, decimal total, int? stars = null, double? review = null, double distance = 1, string? type = null, bool? freeCancellation = null) => new(
        new Hotel { Id = $"bk:{rank}", Name = $"Hotel {rank}", Location = new GeoPoint(0, 0), Stars = stars, ReviewScore = review, StayType = type, FreeCancellation = freeCancellation },
        new HotelPrice(total, total / 4, "USD", new DateOnly(2027, 4, 10), new DateOnly(2027, 4, 14), 2, Now),
        rank,
        distance,
        NearCity: false);

    private static readonly HotelOffer[] Offers =
    [
        Offer(1, 600, stars: 4, review: 8.0, distance: 3, type: "Hotels", freeCancellation: true),
        Offer(2, 300, stars: null, review: 9.1, distance: 1, type: "Apartments"),
        Offer(3, 450, stars: 5, review: null, distance: 2),
        Offer(4, 200, stars: 2, review: 6.5, distance: 9, type: "Hotels", freeCancellation: false),
    ];

    private static IEnumerable<int> Ranks(IEnumerable<HotelOffer> offers) => offers.Select(o => o.Rank);

    [Fact]
    public void Recommended_keeps_the_source_order()
    {
        Assert.Equal([1, 2, 3, 4], Ranks(HotelFilters.Create().Apply(Offers.Reverse())));
    }

    [Theory]
    [InlineData("price", new[] { 4, 2, 3, 1 })]
    [InlineData("REVIEW", new[] { 2, 1, 4, 3 })] // no reviews go last
    [InlineData("stars", new[] { 3, 1, 4, 2 })] // no stars go last
    [InlineData("distance", new[] { 2, 3, 1, 4 })]
    public void Sorts_by_the_chosen_field(string sort, int[] expected)
    {
        Assert.Equal(expected, Ranks(HotelFilters.Create(sort).Apply(Offers)));
    }

    [Fact]
    public void Filters_leave_out_hotels_without_the_value()
    {
        Assert.Equal([1, 3], Ranks(HotelFilters.Create(minStars: 4).Apply(Offers)));
        Assert.Equal([1, 2], Ranks(HotelFilters.Create(minReviewScore: 8).Apply(Offers)));
    }

    [Fact]
    public void Filters_on_price_range_type_and_free_cancellation()
    {
        Assert.Equal([2, 3], Ranks(HotelFilters.Create(minPrice: 300, maxPrice: 450).Apply(Offers)));
        Assert.Equal([1, 4], Ranks(HotelFilters.Create(stayTypes: ["hotels"]).Apply(Offers)));
        Assert.Equal([1], Ranks(HotelFilters.Create(freeCancellation: true).Apply(Offers)));
        Assert.True(HotelFilters.Create(freeCancellation: true).NeedsDetails);
        Assert.False(HotelFilters.Create(minStars: 3).NeedsDetails);
    }

    [Fact]
    public void Pages_hold_twenty_hotels()
    {
        var many = Enumerable.Range(1, 45).Select(r => Offer(r, 100)).ToList();

        Assert.Equal(20, HotelFilters.Create(page: 2).PageOf(many).Count);
        Assert.Equal([41, 42, 43, 44, 45], Ranks(HotelFilters.Create(page: 3).PageOf(many)));
        Assert.Empty(HotelFilters.Create(page: 4).PageOf(many));
    }

    [Theory]
    [InlineData("cheapest", null, null, null, null, null, "sort")]
    [InlineData(null, 6, null, null, null, null, "minStars")]
    [InlineData(null, null, 11.0, null, null, null, "minReview")]
    [InlineData(null, null, null, 500, 100, null, "maxPrice")]
    [InlineData(null, null, null, -1, null, null, "maxPrice")]
    [InlineData(null, null, null, null, null, 0, "page")]
    public void Invalid_values_are_rejected_on_their_field(string? sort, int? minStars, double? minReview, int? minPrice, int? maxPrice, int? page, string field)
    {
        var error = Assert.Throws<DomainException>(() => HotelFilters.Create(sort, minStars, minReview, minPrice, maxPrice, page: page));

        Assert.Equal(field, error.Field);
    }
}
