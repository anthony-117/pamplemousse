using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Domain.Hotels;
using Pamplemousse.Infrastructure.Caching;
using Pamplemousse.Infrastructure.Providers;

namespace Pamplemousse.Infrastructure.Tests.Providers;

public class BookingComHotelProviderTests
{
    private static readonly FixedClock Clock = new(new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero));
    private static readonly Destination Rome = new("3169070", "Rome", "Lazio", "IT", new GeoPoint(41.89, 12.51), 2_318_895, "Europe/Rome");
    private static readonly Destination Zahle = new("268743", "Zahlé", "Beqaa", "LB", new GeoPoint(33.85, 35.9), 50_000, "Asia/Beirut");
    private static readonly CityMapping RomeCity = new("3169070", "loc-rome", -126693, "Rome", Clock.GetUtcNow());

    private static readonly HotelSearch Search = HotelSearch.Create(
        "3169070", DateRange.ForTrip(new DateOnly(2027, 4, 10), new DateOnly(2027, 4, 14), new DateOnly(2026, 11, 3)), adults: 2, currency: "USD");

    // Shaped like real answers from 3 Nov 2026 (Rome search, Paris hotel details), cut down to the fields used.
    private static string Stay(int id, double? gross = 564.33, double excluded = 110.18, int propertyClass = 4, int qualityClass = 0, double review = 8.7, int reviews = 1520) =>
        $$"""
        {"id":{{id}},"name":"Hotel {{id}}","latitude":41.9,"longitude":12.49,"ufi":-126693,"countryCode":"it",
         "propertyClass":{{propertyClass}},"qualityClass":{{qualityClass}},"reviewScore":{{review}},"reviewCount":{{reviews}},"reviewScoreWord":"Excellent",
         "photoUrls":["https://cf.bstatic.com/xdata/images/hotel/square60/928826607.jpg?k=6de7&o="],
         "checkin":{"fromTime":"15:00","untilTime":"17:30"},"checkout":{"fromTime":"10:00","untilTime":"12:00"},
         "priceBreakdown":{{(gross is null ? "{}" : $"{{\"grossPrice\":{{\"value\":{gross},\"currency\":\"USD\"}},\"excludedPrice\":{{\"value\":{excluded},\"currency\":\"USD\"}}}}")}}}
        """;

    private static string Results(int totalPages, params string[] stays) =>
        $$"""{"status":true,"message":"Successful","meta":{"currentPage":1,"limit":20,"totalRecords":100,"totalPage":{{totalPages}}},"data":[{{string.Join(',', stays)}}]}""";

    private const string Details = """
        {"status":true,"data":{"hotel_id":59015,"hotel_name":"Mirific","hotel_name_trans":"Hôtel Mirific","latitude":48.88,"longitude":2.32,
         "url":"https://www.booking.com/hotel/fr/mirific.html","accommodation_type_name":"Hotels","address":"119 Avenue De Clichy","zip":"75017","city":"Paris",
         "district":"17th arr.","ufi":-1456928,"countrycode":"fr","timezone":"Europe/Paris","distance_to_cc":4.30167,"is_city_center":0,
         "hotel_include_breakfast":1,"is_family_friendly":0,"wifi_review_score":{"rating":8.5},"breakfast_review_score":{"rating":8.1},
         "facilities_block":{"facilities":[{"name":"Non-smoking rooms"},{"name":"Free Wifi"}]},"languages_spoken":{"languagecode":["fr","en-gb"]},
         "block":[{"max_occupancy":"1","refundable":1},{"max_occupancy":2,"refundable":0}]}}
        """;

    private const string AutoComplete = """
        {"status":true,"data":[
         {"id":"loc-zahle-hotel","dest_id":"17189511","dest_type":"hotel","name":"Zahle","cc1":"lb","latitude":33.84,"longitude":35.9},
         {"id":"loc-zahle-br","dest_id":"-671824","dest_type":"city","name":"Zahle Jardins","cc1":"br","latitude":-23.57,"longitude":-46.65},
         {"id":"loc-zahle","dest_id":"-812389","dest_type":"city","name":"Zahle","cc1":"lb","latitude":33.8497,"longitude":35.9042}]}
        """;

    private sealed class CountingQuota(bool allow = true) : IRequestQuota
    {
        public int Taken { get; private set; }

        public long Limit { get; private set; }

        public Task<bool> TryTakeAsync(string source, long limit, CancellationToken ct)
        {
            Taken++;
            Limit = limit;
            return Task.FromResult(allow);
        }
    }

    private static BookingComHotelProvider Provider(StubHttp http, string key = "secret-key", IRequestQuota? quota = null) => new(
        http.Client(),
        Options.Create(new ExternalApiOptions { RapidApiKey = key }),
        quota ?? new CountingQuota(),
        Clock);

    [Fact]
    public async Task The_total_adds_the_excluded_taxes_and_counts_per_night()
    {
        var http = new StubHttp().On("/stays/search", Results(5, Stay(1, gross: 564.33, excluded: 110.18)));

        var results = await Provider(http).SearchAsync(RomeCity, Search, 1, CancellationToken.None);

        var found = Assert.Single(results.Hotels);
        Assert.Equal((674.51m, 168.63m, "USD"), (found.Price.Total, found.Price.PerNight, found.Price.Currency));
        Assert.Equal((1, 5), (found.Rank, results.TotalPages));
        Assert.Equal(found.Price, found.Hotel.LastPrice);
    }

    [Fact]
    public async Task Reads_every_search_field_and_keeps_the_raw_entry()
    {
        var http = new StubHttp().On("/stays/search", Results(1, Stay(17458865)));

        var hotel = (await Provider(http).SearchAsync(RomeCity, Search, 1, CancellationToken.None)).Hotels[0].Hotel;

        Assert.Equal(("bk:17458865", "Hotel 17458865", "IT", -126693L), (hotel.Id, hotel.Name, hotel.CountryCode, hotel.CityUfi));
        Assert.Equal((4, false, 8.7, 1520, "Excellent"), (hotel.Stars, hotel.StarsEstimated, hotel.ReviewScore, hotel.ReviewCount, hotel.ReviewWord));
        Assert.Equal(("15:00", "17:30", "10:00", "12:00"), (hotel.CheckInFrom, hotel.CheckInUntil, hotel.CheckOutFrom, hotel.CheckOutUntil));
        Assert.Equal(["https://cf.bstatic.com/xdata/images/hotel/square500/928826607.jpg?k=6de7&o="], hotel.PhotoUrls, StringComparer.Ordinal);
        Assert.Equal(17458865, JsonDocument.Parse(hotel.RawSearch!).RootElement.GetProperty("id").GetInt32());
        Assert.Equal(Clock.GetUtcNow(), hotel.SearchUpdatedAt);
    }

    [Theory]
    [InlineData(4, 0, 4, false)] // official rating
    [InlineData(0, 3, 3, true)] // the source's own estimate
    [InlineData(0, 0, null, null)] // unrated: empty, not 0
    public async Task Stars_are_official_estimated_or_empty(int propertyClass, int qualityClass, int? stars, bool? estimated)
    {
        var http = new StubHttp().On("/stays/search", Results(1, Stay(1, propertyClass: propertyClass, qualityClass: qualityClass)));

        var hotel = (await Provider(http).SearchAsync(RomeCity, Search, 1, CancellationToken.None)).Hotels[0].Hotel;

        Assert.Equal((stars, estimated), (hotel.Stars, hotel.StarsEstimated));
    }

    [Fact]
    public async Task A_hotel_without_reviews_has_no_score()
    {
        var http = new StubHttp().On("/stays/search", Results(1, Stay(1, review: 0, reviews: 0)));

        var hotel = (await Provider(http).SearchAsync(RomeCity, Search, 1, CancellationToken.None)).Hotels[0].Hotel;

        Assert.Equal((null, null, null), (hotel.ReviewScore, hotel.ReviewCount, hotel.ReviewWord));
    }

    [Fact]
    public async Task Hotels_without_a_price_are_left_out_and_repeats_shown_once()
    {
        var http = new StubHttp().On("/stays/search", Results(1, Stay(1), Stay(2, gross: null), Stay(3, gross: 0, excluded: 0), Stay(1)));

        var results = await Provider(http).SearchAsync(RomeCity, Search, 2, CancellationToken.None);

        var found = Assert.Single(results.Hotels);
        Assert.Equal(21, found.Rank); // page 2 starts at 21
    }

    [Fact]
    public async Task Sends_the_search_and_the_key_in_a_header_only()
    {
        var http = new StubHttp().On("/stays/search", Results(1));

        await Provider(http).SearchAsync(RomeCity, Search, 3, CancellationToken.None);

        var url = Assert.Single(http.Requests).ToString();
        Assert.Contains("locationId=loc-rome&checkinDate=2027-04-10&checkoutDate=2027-04-14&adults=2&units=metric&temperature=c&currencyCode=USD&page=3", url, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-key", url, StringComparison.Ordinal);
        Assert.Contains("x-rapidapi-key: secret-key", http.Headers, StringComparer.Ordinal);
        Assert.Contains("x-rapidapi-host: booking-com18.p.rapidapi.com", http.Headers, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Every_request_is_counted_and_stops_at_95_percent_of_the_plan()
    {
        var quota = new CountingQuota();
        var http = new StubHttp().On("/stays/search", Results(1));

        await Provider(http, quota: quota).SearchAsync(RomeCity, Search, 1, CancellationToken.None);

        Assert.Equal((1, 503L), (quota.Taken, quota.Limit));
    }

    [Fact]
    public async Task A_used_up_limit_makes_no_request()
    {
        var http = new StubHttp();

        await Assert.ThrowsAsync<QuotaExceededException>(() => Provider(http, quota: new CountingQuota(allow: false)).SearchAsync(RomeCity, Search, 1, CancellationToken.None));
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task A_missing_key_names_the_variable_and_makes_no_request()
    {
        var http = new StubHttp();

        var error = await Assert.ThrowsAsync<ProviderNotConfiguredException>(() => Provider(http, key: "").SearchAsync(RomeCity, Search, 1, CancellationToken.None));

        Assert.Equal("RAPIDAPI_KEY", error.EnvironmentVariable);
        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "{}")]
    [InlineData(HttpStatusCode.OK, """{"status":false,"message":"Invalid locationId"}""")]
    public async Task Refusals_are_request_failures(HttpStatusCode status, string body)
    {
        var http = new StubHttp().On("/stays/search", body, status);

        await Assert.ThrowsAsync<HttpRequestException>(() => Provider(http).SearchAsync(RomeCity, Search, 1, CancellationToken.None));
    }

    [Fact]
    public async Task A_broken_answer_is_a_json_failure()
    {
        var http = new StubHttp().On("/stays/search", "{\"data\":[");

        await Assert.ThrowsAnyAsync<JsonException>(() => Provider(http).SearchAsync(RomeCity, Search, 1, CancellationToken.None));
    }

    [Fact]
    public async Task Details_give_the_fixed_hotel_fields_and_the_booking_page()
    {
        var http = new StubHttp().On("/stays/detail", Details);

        var hotel = await Provider(http).GetDetailsAsync("bk:59015", Search, CancellationToken.None);

        Assert.NotNull(hotel);
        Assert.Equal(("bk:59015", "Hôtel Mirific", "Hotels"), (hotel.Id, hotel.Name, hotel.StayType));
        Assert.Equal(("119 Avenue De Clichy, 75017 Paris", "17th arr.", "FR", "Europe/Paris"), (hotel.Address, hotel.District, hotel.CountryCode, hotel.TimeZone));
        Assert.Equal((4.3, false, true, false), (hotel.DistanceToCentreKm, hotel.IsCityCentre, hotel.BreakfastIncluded, hotel.FamilyFriendly));
        Assert.Equal((8.1, 8.5), (hotel.BreakfastScore, hotel.WifiScore));
        Assert.Equal(["Non-smoking rooms", "Free Wifi"], hotel.Facilities, StringComparer.Ordinal);
        Assert.Equal(["fr", "en-gb"], hotel.Languages, StringComparer.Ordinal);
        Assert.Equal("https://www.booking.com/hotel/fr/mirific.html", hotel.BookingPageUrl);
        Assert.Equal(Clock.GetUtcNow(), hotel.DetailsUpdatedAt);
        Assert.NotNull(hotel.RawDetails);
        Assert.Contains("hotelId=59015&checkinDate=2027-04-10", http.Requests[0].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_empty_translated_name_falls_back_to_the_name()
    {
        var http = new StubHttp().On("/stays/detail", Details.Replace("\"hotel_name_trans\":\"Hôtel Mirific\"", "\"hotel_name_trans\":\"\"", StringComparison.Ordinal));

        var hotel = await Provider(http).GetDetailsAsync("bk:59015", Search, CancellationToken.None);

        Assert.Equal("Mirific", hotel?.Name);
    }

    [Fact]
    public async Task Free_cancellation_only_counts_rooms_that_fit_the_travellers()
    {
        // The refundable room holds one person; the room for two is not refundable.
        var http = new StubHttp().On("/stays/detail", Details);

        var hotel = await Provider(http).GetDetailsAsync("bk:59015", Search, CancellationToken.None);

        Assert.False(hotel?.FreeCancellation);
    }

    [Fact]
    public async Task Ids_from_another_source_are_unknown_without_a_request()
    {
        var http = new StubHttp();

        Assert.Null(await Provider(http).GetDetailsAsync("tp:1", Search, CancellationToken.None));
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task The_city_is_the_nearest_city_of_the_same_country()
    {
        var http = new StubHttp().On("/stays/auto-complete", AutoComplete);

        var city = await Provider(http).FindCityAsync(Zahle, CancellationToken.None);

        Assert.Equal(("268743", "loc-zahle", -812389L, "Zahle"), (city?.DestinationId, city?.LocationId, city?.Ufi, city?.SourceName));
        Assert.Contains("query=Zahl%C3%A9", http.Requests[0].AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_city_nearby_is_no_city()
    {
        var http = new StubHttp().On("/stays/auto-complete", AutoComplete);

        Assert.Null(await Provider(http).FindCityAsync(Rome, CancellationToken.None));
    }

    [Fact]
    public void The_booking_link_fills_in_the_dates_and_travellers()
    {
        var provider = Provider(new StubHttp());
        var hotel = new Hotel { Id = "bk:59015", Name = "Mirific", Location = default, BookingPageUrl = "https://www.booking.com/hotel/fr/mirific.html" };

        Assert.Equal(
            "https://www.booking.com/hotel/fr/mirific.html?checkin=2027-04-10&checkout=2027-04-14&group_adults=2&no_rooms=1&group_children=0&selected_currency=USD",
            provider.BookingUrl(hotel, Search));
        Assert.StartsWith(
            "https://www.booking.com/searchresults.html?dest_id=59015&dest_type=hotel&checkin=2027-04-10",
            provider.BookingUrl(hotel with { BookingPageUrl = null }, Search),
            StringComparison.Ordinal);
        Assert.StartsWith("https://www.booking.com/searchresults.html?ss=Rome&checkin=", provider.SearchUrl(Rome, Search), StringComparison.Ordinal);
    }
}
