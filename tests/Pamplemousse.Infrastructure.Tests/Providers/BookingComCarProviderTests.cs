using System.IO.Compression;
using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Cars;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Infrastructure.Caching;
using Pamplemousse.Infrastructure.Providers;

namespace Pamplemousse.Infrastructure.Tests.Providers;

public class BookingComCarProviderTests
{
    private static readonly Destination Rome = new("3169070", "Rome", "Lazio", "IT", new GeoPoint(41.89, 12.51), 2_318_895, "Europe/Rome");
    private static readonly DateRange Dates = DateRange.ForTrip(new DateOnly(2026, 12, 4), new DateOnly(2026, 12, 8), new DateOnly(2026, 11, 5));
    private static readonly PickUpPlace Centre = PickUpPlace.Centre(new GeoPoint(41.89814758300781, 12.481033325195312));
    private static readonly PickUpPlace Fiumicino = PickUpPlace.Airport("FCO", "Fiumicino Airport", new GeoPoint(41.7927, 12.2523));

    // Shaped like the real Rome answers from 5 Nov 2026, cut down to the fields used.
    private const string AutoComplete = """
        {"status":true,"message":"Success","data":[
         {"name":"Fiumicino Airport","type":"airport","coordinates":{"latitude":41.792701721191406,"longitude":12.252300262451172},"iata_code":"FCO"},
         {"name":"Rome City Centre","type":"district","coordinates":{"latitude":41.898,"longitude":12.481},"iata_code":null},
         {"name":"Rome Termini Train Station","type":null,"coordinates":{"latitude":41.901,"longitude":12.501},"iata_code":null},
         {"name":"Greater Moncton Roméo LeBlanc Airport","type":"airport","coordinates":{"latitude":46.1122,"longitude":-64.6786},"iata_code":"YQM"},
         {"name":"Airport without a code","type":"airport","coordinates":{"latitude":41.8,"longitude":12.6},"iata_code":null}]}
        """;

    private static string Car(string id, string model = "Fiat 600", string price = "\"drive_away_price\":68.32,\"drive_away_price_before\":76.76", string bags = "3 checked bags") =>
        $$$"""
        {"content":{"vehicleSpecs":[{"text":"Automatic","icon":"TRANSMISSION_AUTOMATIC"},{"text":"{{{bags}}}","icon":"BAGS_LARGE"}]},
         "forward_url":"https://cars.booking.com/DriverExtras.do?vehicleInfo.vehicle.id={{{id}}}",
         "pricing_info":{"currency":"USD",{{{price}}},"pay_when":"PAY_NOW",
          "fee_breakdown":{"known_fees":[{"amount":3294.0,"currency":"EUR","type":"DAMAGE_EXCESS"},{"amount":200.0,"currency":"EUR","type":"DEPOSIT"},{"amount":null,"currency":null,"type":"MILEAGE"}]}},
         "rating_info":{"average":7.0,"no_of_ratings":372},
         "route_info":{"pickup":{"address":"38 A, Via Sardegna, Rome Center, Roma, Italy, 00187","latitude":"41.9095","longitude":"12.491223","location_type":"DOWNTOWN","name":"Rome - via Sardegna"}},
         "supplier_info":{"name":"Budget","logo_url":"https://cdn.rcstatic.com/sp/images/suppliers/62_logo_200.png"},
         "vehicle_info":{"aircon":1,"badges":{"sponsored":"Ad"},"doors":"4","free_cancellation":1,"fuel_policy":"Like for like","group":"Intermediate",
          "group_or_similar":"or similar large car","image_url":"https://cdn.rcstatic.com/images/car_images/new_images/fiat/600_lrg.png","mileage":"Unlimited km",
          "seats":"5","suitcases":{"big":"0","small":"0"},"transmission":"Automatic","unlimited_mileage":1,"v_id":"{{{id}}}","v_name":"{{{model}}}"}}
        """;

    private const string Plain = """
        {"pricing_info":{"currency":"USD","drive_away_price":120.0,"pay_when":"PAY_LOCAL"},
         "route_info":{"pickup":{"latitude":"41.79","longitude":"12.25","location_type":"IN_TERMINAL","name":"Fiumicino Airport"}},
         "supplier_info":{"name":"Locauto"},
         "vehicle_info":{"seats":"5+","transmission":"Manual","v_id":"777","v_name":"Fiat Panda","badges":{}}}
        """;

    private static string Results(params string[] cars) =>
        $$$"""{"status":true,"message":"Success","data":{"count":{{{cars.Length}}},"search_results":[{{{string.Join(',', cars)}}}]}}""";

    private sealed class CountingQuota(bool allow = true) : IRequestQuota
    {
        public int Taken { get; private set; }

        public Task<bool> TryTakeAsync(string source, long limit, CancellationToken ct)
        {
            Taken++;
            return Task.FromResult(allow);
        }
    }

    private static BookingComCarProvider Provider(StubHttp http, string key = "secret-key", IRequestQuota? quota = null) => new(
        http.Client(), Options.Create(new ExternalApiOptions { RapidApiKey = key }), quota ?? new CountingQuota());

    private static CarSearch Search(PickUpPlace? place = null, int? age = null) => CarSearch.Create("3169070", place ?? Centre, Dates, age, "usd");

    [Fact]
    public async Task Airports_are_the_results_with_an_airport_code()
    {
        var http = new StubHttp().On("/car/auto-complete", AutoComplete);

        var airports = await Provider(http).FindAirportsAsync(Rome, CancellationToken.None);

        Assert.Equal(["FCO", "YQM"], airports.Select(a => a.Id), StringComparer.Ordinal);
        Assert.Equal(("Fiumicino Airport", 41.792701721191406), (airports[0].Name, airports[0].Location.Lat));
        Assert.Contains("query=Rome", http.Requests[0].Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_search_names_the_place_by_its_coordinates_at_ten()
    {
        var http = new StubHttp().On("/car/search", Results());

        await Provider(http).SearchAsync(Search(age: 45), CancellationToken.None);

        var query = Uri.UnescapeDataString(http.Requests[0].Query);
        var id = BookingComCarProvider.PlaceId(Centre.Location);
        Assert.Equal("""{"latitude":"41.89814758300781","longitude":"12.481033325195312"}""", Encoding.UTF8.GetString(Convert.FromBase64String(id)));
        Assert.Contains($"pickUpId={id}&dropOffId={id}", query, StringComparison.Ordinal);
        Assert.Contains("pickUpDate=2026-12-04&pickUpTime=10:00&dropOffDate=2026-12-08&dropOffTime=10:00&driverAge=45&currencyCode=USD", query, StringComparison.Ordinal);
        Assert.Contains("x-rapidapi-host: booking-com18.p.rapidapi.com", http.Headers, StringComparer.Ordinal);
    }

    [Fact]
    public async Task A_car_reads_with_the_normal_price_and_every_field()
    {
        var http = new StubHttp().On("/car/search", Results(Car("858416088")));

        var results = await Provider(http).SearchAsync(Search(), CancellationToken.None);

        var car = Assert.Single(results.Cars);
        Assert.Equal(("bk:858416088", "Fiat 600", "or similar large car", "Intermediate"), (car.Id, car.Model, car.OrSimilar, car.Size));
        Assert.Equal((76.76m, 19.19m, "USD", true), (car.Price.Total, car.Price.PerDay, car.Price.Currency, car.Price.PayNow));
        Assert.Equal((Gearbox.Automatic, 5, 4, 3, true), (car.Gearbox, car.Seats, car.Doors, car.Bags, car.AirCon));
        Assert.Equal(("Unlimited km", true, "Like for like", true), (car.Mileage, car.UnlimitedMileage, car.FuelPolicy, car.FreeCancellation));
        Assert.Equal(new RentalCompany("Budget", "https://cdn.rcstatic.com/sp/images/suppliers/62_logo_200.png", 7.0, 372), car.Company);
        Assert.Equal(("Rome - via Sardegna", PickUpType.Downtown, 41.9095), (car.PickUp.Name, car.PickUp.Type, car.PickUp.Location.Lat));
        Assert.Equal((new Money(200, "EUR"), new Money(3294, "EUR")), (car.Deposit, car.DamageExcess));
        Assert.Equal((true, 1), (car.Sponsored, car.Rank));
        Assert.False(results.MayHaveMore);
    }

    [Fact]
    public async Task Missing_fields_stay_unknown()
    {
        var http = new StubHttp().On("/car/search", Results(Plain));

        var car = Assert.Single((await Provider(http).SearchAsync(Search(), CancellationToken.None)).Cars);

        Assert.Equal((120m, false, PickUpType.Terminal, Gearbox.Manual), (car.Price.Total, car.Price.PayNow, car.PickUp.Type, car.Gearbox));
        Assert.Null(car.Seats); // "5+" is not a count
        Assert.Null(car.Bags);
        Assert.Null(car.Company.Rating);
        Assert.Null(car.Deposit);
        Assert.Null(car.FreeCancellation);
        Assert.False(car.Sponsored);
    }

    [Fact]
    public async Task Cars_without_a_price_or_seen_twice_are_left_out_in_source_order()
    {
        var http = new StubHttp().On(
            "/car/search",
            Results(Car("1"), Car("2", price: "\"drive_away_price\":0"), Car("1", model: "Again"), Car("3", model: "VW Golf")));

        var cars = (await Provider(http).SearchAsync(Search(), CancellationToken.None)).Cars;

        Assert.Equal(["bk:1", "bk:3"], cars.Select(c => c.Id), StringComparer.Ordinal);
        Assert.Equal([1, 4], cars.Select(c => c.Rank));
    }

    [Fact]
    public async Task Five_hundred_cars_may_have_left_some_out()
    {
        var http = new StubHttp().On("/car/search", Results([.. Enumerable.Range(1, 500).Select(i => Car(i.ToString(System.Globalization.CultureInfo.InvariantCulture)))]));

        var results = await Provider(http).SearchAsync(Search(), CancellationToken.None);

        Assert.True(results.MayHaveMore);
        Assert.Equal(500, results.Cars.Count);
    }

    [Fact]
    public async Task The_whole_answer_is_kept_gzip_compressed()
    {
        var body = Results(Car("1"));
        var http = new StubHttp().On("/car/search", body);

        var results = await Provider(http).SearchAsync(Search(), CancellationToken.None);

        using var gzip = new GZipStream(new MemoryStream(results.Raw), CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        Assert.Equal(body, await reader.ReadToEndAsync());
    }

    [Fact]
    public void The_booking_link_opens_the_same_search_on_booking_com()
    {
        var provider = Provider(new StubHttp());

        var centre = provider.SearchUrl(Rome, Search(age: 45));
        var airport = provider.SearchUrl(Rome, Search(Fiumicino));

        Assert.StartsWith("https://cars.booking.com/search-results?", centre, StringComparison.Ordinal);
        Assert.Contains("locationName=Rome&dropLocationName=Rome&", centre, StringComparison.Ordinal);
        Assert.Contains("coordinates=41.89814758300781,12.481033325195312&dropCoordinates=41.89814758300781,12.481033325195312", centre, StringComparison.Ordinal);
        Assert.Contains("&ftsType=D&dropFtsType=D&driversAge=45&puDay=4&puMonth=12&puYear=2026&puHour=10&puMinute=0&doDay=8&doMonth=12&doYear=2026&doHour=10&doMinute=0&prefcurrency=USD", centre, StringComparison.Ordinal);
        Assert.Contains("locationName=Fiumicino%20Airport", airport, StringComparison.Ordinal);
        Assert.Contains("&ftsType=A&dropFtsType=A", airport, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_key_nothing_is_sent()
    {
        var http = new StubHttp();

        var ex = await Assert.ThrowsAsync<ProviderNotConfiguredException>(() => Provider(http, key: "").SearchAsync(Search(), CancellationToken.None));

        Assert.Contains("RAPIDAPI_KEY", ex.Message, StringComparison.Ordinal);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task Near_the_monthly_limit_nothing_is_sent()
    {
        var http = new StubHttp();

        await Assert.ThrowsAsync<QuotaExceededException>(() => Provider(http, quota: new CountingQuota(allow: false)).FindAirportsAsync(Rome, CancellationToken.None));

        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task A_refusal_inside_a_200_answer_is_a_failure()
    {
        var http = new StubHttp().On("/car/search", """{"status":false,"message":"Invalid pickUpId"}""");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Provider(http).SearchAsync(Search(), CancellationToken.None));

        Assert.Contains("Invalid pickUpId", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_error_status_is_a_failure()
    {
        var http = new StubHttp().On("/car/search", "{}", HttpStatusCode.TooManyRequests);

        await Assert.ThrowsAsync<HttpRequestException>(() => Provider(http).SearchAsync(Search(), CancellationToken.None));
    }
}
