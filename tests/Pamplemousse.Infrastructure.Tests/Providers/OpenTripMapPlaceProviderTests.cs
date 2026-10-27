using System.Globalization;
using System.Net;
using Pamplemousse.Domain.Abstractions;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Domain.Places;
using Pamplemousse.Infrastructure.Providers;

namespace Pamplemousse.Infrastructure.Tests.Providers;

public class OpenTripMapPlaceProviderTests
{
    private static readonly Destination Rome = new("3169070", "Rome", "Lazio", "IT", new GeoPoint(41.8919, 12.5113), 2_318_895, "Europe/Rome");

    // Shaped like Rome's real data: the nearest results to the city point are minor sights,
    // the Colosseum appears twice, and a restaurant in a historic building is tagged as an interesting place.
    private const string Bbox = """
        [{"xid":"W1","name":"Aurelian Walls","kinds":"fortifications,historic,interesting_places","rate":7,"wikidata":"Q625149","point":{"lon":12.5150,"lat":41.8930}},
         {"xid":"N2","name":"","kinds":"interesting_places","rate":3,"wikidata":"Q1","point":{"lon":12.5,"lat":41.9}},
         {"xid":"W3","name":"Colosseum","kinds":"amphitheatres,historic_architecture,interesting_places","rate":"3h","wikidata":"Q10285","point":{"lon":12.4922,"lat":41.8902}},
         {"xid":"W4","name":"Colosseum","kinds":"architecture,interesting_places","rate":3,"wikidata":"Q10285","point":{"lon":12.4925,"lat":41.8900}},
         {"xid":"N5","name":"Trevi Fountain","kinds":"fountains,historic,interesting_places","rate":3,"wikidata":"Q185382","point":{"lon":12.4833,"lat":41.9009}},
         {"xid":"N6","name":"Il Tempio di Minerva","kinds":"historic_architecture,interesting_places,restaurants,foods","rate":3,"wikidata":"Q1411575","point":{"lon":12.5150,"lat":41.8950}},
         {"xid":"N7","name":"Small fountain","kinds":"fountains,interesting_places","rate":3,"point":{"lon":12.5100,"lat":41.8920}}]
        """;

    internal static OpenTripMapAttractionProvider Attractions(StubHttp http) => new(http.Client(), TestDoubles.Cache(), TestDoubles.Keys());

    internal static OpenTripMapAttractionProvider Attractions(StubHttp http, string openTripMapKey) =>
        new(http.Client(), TestDoubles.Cache(), TestDoubles.Keys(openTripMap: openTripMapKey));

    [Fact]
    public async Task Attractions_are_unique_ranked_by_rate_then_distance()
    {
        var http = new StubHttp().On("/bbox", Bbox);

        var places = await Attractions(http).SearchAsync(Rome, 10, CancellationToken.None);

        // Unnamed place dropped, the restaurant left to the restaurant list, the Colosseum once; 7 > "3h" > 3, then nearest.
        Assert.Equal(["Aurelian Walls", "Colosseum", "Small fountain", "Trevi Fountain"], places.Select(p => p.Name), StringComparer.Ordinal);
        Assert.Equal("otm:W3", places[1].Id); // the heritage-rated copy wins
        Assert.Equal("Amphitheatres", places[1].Kind);
        Assert.Equal(PlaceCategory.Attraction, places[1].Category);
        Assert.Equal(new GeoPoint(41.8902, 12.4922), places[1].Location);
    }

    [Fact]
    public async Task Attractions_are_searched_over_the_whole_city_area()
    {
        var http = new StubHttp().On("/bbox", "[]");

        await Attractions(http).SearchAsync(Rome, 10, CancellationToken.None);

        var query = http.Requests[0].Query;
        Assert.Contains("kinds=interesting_places&rate=3", query, StringComparison.Ordinal);
        Assert.Contains("limit=500", query, StringComparison.Ordinal);
        Assert.Contains("apikey=otm-key", query, StringComparison.Ordinal);
        // A square of about 20 km around the city point.
        var latMin = Param(query, "lat_min");
        var latMax = Param(query, "lat_max");
        var lonMin = Param(query, "lon_min");
        var lonMax = Param(query, "lon_max");
        Assert.InRange((latMax - latMin) * 111.32, 19.9, 20.1);
        Assert.InRange((lonMax - lonMin) * 111.32 * Math.Cos(41.8919 * Math.PI / 180), 19.9, 20.1);
        Assert.InRange(Rome.Location.Lat, latMin, latMax);
        Assert.InRange(Rome.Location.Lng, lonMin, lonMax);
    }

    [Fact]
    public async Task Restaurants_use_the_foods_category_once_per_name()
    {
        var http = new StubHttp().On("/radius", """
            [{"xid":"N9","name":"Da Enzo","kinds":"foods,restaurants","point":{"lon":12.47,"lat":41.88}},
             {"xid":"N10","name":"Da Enzo","kinds":"foods,restaurants","point":{"lon":12.47,"lat":41.88}},
             {"xid":"N6","name":"Il Tempio di Minerva","kinds":"historic_architecture,interesting_places,restaurants,foods","point":{"lon":12.515,"lat":41.895}}]
            """);
        var provider = new OpenTripMapRestaurantProvider(http.Client(), TestDoubles.Cache(), TestDoubles.Keys());

        var places = await provider.SearchAsync(Rome, 5, CancellationToken.None);

        Assert.Equal(["Il Tempio di Minerva", "Da Enzo"], places.Select(p => p.Name), StringComparer.Ordinal); // nearest first
        Assert.All(places, p => Assert.Equal(PlaceCategory.Restaurant, p.Category));
        Assert.Equal("Restaurants", places[0].Kind); // not "Historic architecture"
        Assert.Equal("Restaurants", places[1].Kind);
        Assert.Contains("kinds=foods", http.Requests[0].Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Details_include_description_address_image_and_detect_restaurants()
    {
        var http = new StubHttp().On("/xid/N9", """
            {"xid":"N9","name":"Da Enzo","kinds":"foods,restaurants","point":{"lon":12.47,"lat":41.88},
             "address":{"road":"Via dei Vascellari","house_number":"29","city":"Rome"},
             "wikipedia_extracts":{"text":"Roman trattoria."},"preview":{"source":"https://img/enzo.jpg"},"url":"https://daenzo.it"}
            """);

        var place = await Attractions(http).GetAsync("N9", CancellationToken.None);

        Assert.NotNull(place);
        Assert.Equal("otm:N9", place.Id);
        Assert.Equal(PlaceCategory.Restaurant, place.Category);
        Assert.Equal("Via dei Vascellari, 29, Rome", place.Address);
        Assert.Equal("Roman trattoria.", place.Description);
        Assert.Equal("https://img/enzo.jpg", place.ImageUrl);
        Assert.Equal("https://daenzo.it", place.Website);
    }

    [Fact]
    public async Task Unknown_place_returns_null()
    {
        var http = new StubHttp().On("/xid/", "{}", HttpStatusCode.NotFound);

        Assert.Null(await Attractions(http).GetAsync("nope", CancellationToken.None));
    }

    [Fact]
    public async Task Missing_key_is_reported_without_calling_the_api()
    {
        var http = new StubHttp();
        var provider = new OpenTripMapRestaurantProvider(http.Client(), TestDoubles.Cache(), TestDoubles.Keys(openTripMap: ""));

        var error = await Assert.ThrowsAsync<ProviderNotConfiguredException>(() => provider.SearchAsync(Rome, 5, CancellationToken.None));

        Assert.Equal("OPENTRIPMAP_API_KEY", error.EnvironmentVariable);
        Assert.Empty(http.Requests);
    }

    private static double Param(string query, string name) => double.Parse(
        query.TrimStart('?').Split('&').Select(p => p.Split('=')).Single(p => string.Equals(p[0], name, StringComparison.Ordinal))[1],
        CultureInfo.InvariantCulture);
}
