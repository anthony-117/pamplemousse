using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Domain.Hotels;

namespace Pamplemousse.Api.Tests;

/// <summary>Stands in for the real hotel source, so endpoint tests never call the internet or use the request quota.</summary>
public sealed class FakeHotels : IHotelProvider
{
    public Exception? Error { get; set; }

    public int Searches { get; private set; }

    public string Source => "Booking.com";

    public Task<CityMapping?> FindCityAsync(Destination destination, CancellationToken ct) =>
        Error is null
            ? Task.FromResult<CityMapping?>(new CityMapping(destination.Id, "loc", -1, destination.Name, DateTimeOffset.UtcNow))
            : Task.FromException<CityMapping?>(Error);

    public Task<HotelResults> SearchAsync(CityMapping city, HotelSearch search, int page, CancellationToken ct)
    {
        Searches++;
        if (Error is not null)
        {
            return Task.FromException<HotelResults>(Error);
        }

        var now = DateTimeOffset.UtcNow;
        IReadOnlyList<FoundHotel> hotels =
        [
            Found("bk:1", "Hotel Roma", 900, stars: 4, review: 8.8, search, now),
            Found("bk:2", "Budget Rooms", 400, stars: null, review: 7.1, search, now),
        ];
        return Task.FromResult(new HotelResults(hotels, 1));
    }

    public Task<Hotel?> GetDetailsAsync(string hotelId, HotelSearch search, CancellationToken ct) =>
        Error is not null
            ? Task.FromException<Hotel?>(Error)
            : Task.FromResult<Hotel?>(hotelId is "bk:1" or "bk:2"
                ? new Hotel
                {
                    Id = hotelId,
                    Name = "Hotel Roma",
                    Location = new GeoPoint(41.9, 12.5),
                    StayType = "Hotels",
                    FreeCancellation = true,
                    Facilities = ["Free WiFi"],
                    BookingPageUrl = "https://www.booking.com/hotel/it/roma.html",
                    DetailsUpdatedAt = DateTimeOffset.UtcNow,
                }
                : null);

    public string SearchUrl(Destination destination, HotelSearch search) => "https://www.booking.com/searchresults.html?ss=" + destination.Name;

    public string BookingUrl(Hotel hotel, HotelSearch search) =>
        $"{hotel.BookingPageUrl ?? "https://www.booking.com/searchresults.html"}?checkin={search.Dates.From:yyyy-MM-dd}&group_adults={search.Adults}";

    private static FoundHotel Found(string id, string name, decimal total, int? stars, double review, HotelSearch search, DateTimeOffset now)
    {
        var price = HotelPrice.ForStay(total, search, now)!;
        var hotel = new Hotel
        {
            Id = id,
            Name = name,
            Location = new GeoPoint(41.9, 12.49),
            Stars = stars,
            ReviewScore = review,
            ReviewCount = 100,
            PhotoUrls = ["https://cf.bstatic.com/square500/1.jpg"],
            LastPrice = price,
            SearchUpdatedAt = now,
        };
        return new FoundHotel(hotel, price, id is "bk:1" ? 1 : 2);
    }
}
