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

    private const string Radius = """
        [{"xid":"N1","name":"Small fountain","kinds":"fountains,interesting_places","rate":2,"point":{"lon":12.5,"lat":41.9},"dist":100},
         {"xid":"N2","name":"","kinds":"interesting_places","rate":3,"point":{"lon":12.5,"lat":41.9},"dist":50},
         {"xid":"W3","name":"Colosseum","kinds":"amphitheatres,historic_architecture,interesting_places","rate":"3h","point":{"lon":12.4922,"lat":41.8902},"dist":900},
         {"xid":"N4","name":"Pantheon","kinds":"churches,interesting_places","rate":3,"point":{"lon":12.4768,"lat":41.8986},"dist":2000}]
        """;

    [Fact]
    public async Task Attractions_are_named_ranked_by_popularity_and_labelled()
    {
        var http = new StubHttp().On("/radius", Radius);
        var provider = new OpenTripMapAttractionProvider(http.Client(), TestDoubles.Cache(), TestDoubles.Keys());

        var places = await provider.SearchAsync(Rome, 5, CancellationToken.None);

        Assert.Equal(["Colosseum", "Pantheon", "Small fountain"], places.Select(p => p.Name), StringComparer.Ordinal); // unnamed one dropped; "3h" > 3 > 2
        Assert.Equal("otm:W3", places[0].Id);
        Assert.Equal("Amphitheatres", places[0].Kind);
        Assert.Equal(PlaceCategory.Attraction, places[0].Category);
        Assert.Equal(new GeoPoint(41.8902, 12.4922), places[0].Location);
        Assert.Contains("kinds=interesting_places&rate=3", http.Requests[0].Query, StringComparison.Ordinal);
        Assert.Contains("apikey=otm-key", http.Requests[0].Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Restaurants_use_the_foods_category()
    {
        var http = new StubHttp().On("/radius", """[{"xid":"N9","name":"Da Enzo","kinds":"foods,restaurants","point":{"lon":12.47,"lat":41.88}}]""");
        var provider = new OpenTripMapRestaurantProvider(http.Client(), TestDoubles.Cache(), TestDoubles.Keys());

        var place = Assert.Single(await provider.SearchAsync(Rome, 5, CancellationToken.None));

        Assert.Equal(PlaceCategory.Restaurant, place.Category);
        Assert.Equal("Restaurants", place.Kind);
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
        var provider = new OpenTripMapAttractionProvider(http.Client(), TestDoubles.Cache(), TestDoubles.Keys());

        var place = await provider.GetAsync("N9", CancellationToken.None);

        Assert.NotNull(place);
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
        var provider = new OpenTripMapAttractionProvider(http.Client(), TestDoubles.Cache(), TestDoubles.Keys());

        Assert.Null(await provider.GetAsync("nope", CancellationToken.None));
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
}
