using Microsoft.Extensions.Logging.Abstractions;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Hotels.Queries.GetHotel;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Hotels;

namespace Pamplemousse.Application.Tests.Hotels;

public class GetHotelHandlerTests
{
    private readonly FakeHotelProvider _provider = new()
    {
        Details = new Hotel
        {
            Id = "",
            Name = "Hotel 1",
            Location = new GeoPoint(41.9, 12.5),
            StayType = "Hotels",
            FreeCancellation = true,
            BookingPageUrl = "https://www.booking.com/hotel/it/one.html",
            DetailsUpdatedAt = HotelFakes.Now,
        },
    };

    private readonly FakeHotelData _store = new();
    private readonly MovableClock _clock = new();

    private GetHotelHandler Handler() => new(_provider, _store, _store, _store, _clock, NullLogger<GetHotelHandler>.Instance);

    private static GetHotelQuery Query(int? adults = null) => new("bk:1", new DateOnly(2027, 4, 10), new DateOnly(2027, 4, 14), adults, null);

    [Fact]
    public async Task Opening_a_hotel_fetches_its_details_once_and_keeps_its_search_price()
    {
        var searched = HotelFakes.Found(1, 800).Hotel;
        _store.Hotels[searched.Id] = searched;

        var first = await Handler().HandleAsync(Query(), CancellationToken.None);
        var second = await Handler().HandleAsync(Query(), CancellationToken.None);

        Assert.Equal(1, _provider.DetailCalls);
        Assert.Equal(("Hotels", true), (second.Hotel.StayType, second.Hotel.FreeCancellation));
        Assert.Equal(800m, first.Price?.Total);
        Assert.Equal("https://book/bk:1?adults=2", second.BookingUrl);
        Assert.Equal("{}", _store.Hotels["bk:1"].RawSearch);
    }

    [Fact]
    public async Task Details_older_than_a_week_are_fetched_again()
    {
        await Handler().HandleAsync(Query(), CancellationToken.None);
        _clock.Now = HotelFakes.Now.AddDays(8);

        await Handler().HandleAsync(Query(), CancellationToken.None);

        Assert.Equal(2, _provider.DetailCalls);
    }

    [Fact]
    public async Task A_price_for_other_travellers_is_not_shown()
    {
        _store.Hotels["bk:1"] = HotelFakes.Found(1, 800).Hotel;

        var result = await Handler().HandleAsync(Query(adults: 3), CancellationToken.None);

        Assert.Null(result.Price);
    }

    [Fact]
    public async Task When_details_fail_the_stored_hotel_is_returned_with_a_warning()
    {
        _store.Hotels["bk:1"] = HotelFakes.Found(1, 800).Hotel;
        _provider.Error = new QuotaExceededException("Booking COM API");

        var result = await Handler().HandleAsync(Query(), CancellationToken.None);

        Assert.Equal("Hotel 1", result.Hotel.Name);
        Assert.Contains(result.Warnings, w => w.Contains("request limit", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_hotel_nobody_knows_is_not_found()
    {
        _provider.Details = null;

        await Assert.ThrowsAsync<NotFoundException>(() => Handler().HandleAsync(Query(), CancellationToken.None));
    }
}
