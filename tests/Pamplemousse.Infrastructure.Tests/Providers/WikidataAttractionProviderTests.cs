using System.Globalization;
using System.Net;
using Pamplemousse.Domain.Abstractions;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Domain.Places;
using Pamplemousse.Infrastructure.Caching;
using Pamplemousse.Infrastructure.Providers;

namespace Pamplemousse.Infrastructure.Tests.Providers;

public class WikidataAttractionProviderTests
{
    private static readonly Destination Rome = new("3169070", "Rome", "Lazio", "IT", new GeoPoint(41.8919, 12.5113), 2_318_895, "Europe/Rome");

    /// <summary>Shaped like Rome's real answer: famous places mixed with institutions, districts and demolished buildings.</summary>
    private static readonly string Landmarks = Bindings(
        Row("Q10285", "Colosseum", 148, "Roman amphitheatre", 12.4922, 41.8902),
        Row("Q159583", "Holy See", 92, "subject of international law", 12.4533, 41.9022),
        Row("Q99309", "Pantheon", 81, "minor basilica", 12.4769, 41.8986),
        Row("Q99309", "Pantheon", 81, "tourist attraction", 12.4769, 41.8986),
        Row("Q185382", "Trevi Fountain", 68, "fountain", 12.4833, 41.9009),
        Row("Q1", "Monti", 60, "rione of Rome", 12.49, 41.89, extra: "\"population\":{\"value\":\"13000\"}"),
        Row("Q2", "Old Gate", 59, "city gate", 12.49, 41.89, extra: "\"demolished\":{\"value\":\"1890\"}"),
        Row("Q3", null, 58, "museum", 12.49, 41.89),
        Row("Q4", "State Archives", 57, "archives", 12.49, 41.89),
        Row("Q5", "Venus statue", 56, "archaeological artefact", 12.49, 41.89),
        Row("Q6", "Trajan's Column", 55, "archaeological site", 12.4842, 41.8958));

    [Fact]
    public async Task Famous_attractions_come_first_and_other_famous_things_are_left_out()
    {
        var http = new StubHttp().On("query.wikidata.org", Landmarks);

        var places = await Provider(http).SearchAsync(Rome, 4, CancellationToken.None);

        // Not attractions: an institution, a district (population), a demolished gate, an unnamed item,
        // "archives" (not "arch") and an object kept in a museum.
        Assert.Equal(["Colosseum", "Pantheon", "Trevi Fountain", "Trajan's Column"], places.Select(p => p.Name), StringComparer.Ordinal);
        var colosseum = places[0];
        Assert.Equal("wd:Q10285", colosseum.Id);
        Assert.Equal("Roman amphitheatre", colosseum.Kind);
        Assert.Equal(new GeoPoint(41.8902, 12.4922), colosseum.Location);
        Assert.Equal(PlaceCategory.Attraction, colosseum.Category);
        Assert.Equal("Wikidata", colosseum.Source);
        Assert.Equal("Minor basilica", places[1].Kind);
        Assert.All(http.Requests, r => Assert.Equal("query.wikidata.org", r.Host)); // enough landmarks: OpenTripMap is not asked
    }

    [Fact]
    public async Task The_query_searches_around_the_city_and_keeps_well_known_places()
    {
        var http = new StubHttp().On("query.wikidata.org", Landmarks);

        await Provider(http).SearchAsync(Rome, 3, CancellationToken.None);

        var query = Uri.UnescapeDataString(http.Requests[0].Query);
        Assert.Contains("Point(12.5113 41.8919)", query, StringComparison.Ordinal);
        Assert.Contains("wikibase:radius \"10\"", query, StringComparison.Ordinal);
        Assert.Contains("FILTER(?articles >= 15)", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Few_landmarks_are_topped_up_from_opentripmap_without_repeating_one()
    {
        var http = new StubHttp()
            .On("query.wikidata.org", Bindings(Row("Q10285", "Colosseum", 148, "Roman amphitheatre", 12.4922, 41.8902)))
            .On("/bbox", """
                [{"xid":"W3","name":"Colosseum","kinds":"amphitheatres,interesting_places","rate":7,"point":{"lon":12.4922,"lat":41.8902}},
                 {"xid":"W1","name":"Aurelian Walls","kinds":"fortifications,interesting_places","rate":7,"point":{"lon":12.515,"lat":41.893}}]
                """);

        var places = await Provider(http).SearchAsync(Rome, 3, CancellationToken.None);

        Assert.Equal(["wd:Q10285", "otm:W1"], places.Select(p => p.Id), StringComparer.Ordinal);
    }

    [Fact]
    public async Task When_wikidata_is_down_attractions_come_from_opentripmap()
    {
        var http = new StubHttp()
            .On("query.wikidata.org", "Too Many Requests", HttpStatusCode.TooManyRequests)
            .On("/bbox", """[{"xid":"W1","name":"Aurelian Walls","kinds":"fortifications,interesting_places","rate":7,"point":{"lon":12.515,"lat":41.893}}]""");

        var place = Assert.Single(await Provider(http).SearchAsync(Rome, 3, CancellationToken.None));

        Assert.Equal("otm:W1", place.Id);
    }

    [Fact]
    public async Task When_wikidata_is_down_and_opentripmap_has_no_key_the_missing_key_is_reported()
    {
        var http = new StubHttp().On("query.wikidata.org", "busy", HttpStatusCode.ServiceUnavailable);
        var provider = new WikidataAttractionProvider(http.Client(), TestDoubles.Cache(), OpenTripMapPlaceProviderTests.Attractions(http, openTripMapKey: ""));

        var error = await Assert.ThrowsAsync<ProviderNotConfiguredException>(() => provider.SearchAsync(Rome, 3, CancellationToken.None));

        Assert.Equal("OPENTRIPMAP_API_KEY", error.EnvironmentVariable);
    }

    [Fact]
    public async Task Landmarks_found_without_an_opentripmap_key_are_still_shown()
    {
        var http = new StubHttp().On("query.wikidata.org", Bindings(Row("Q10285", "Colosseum", 148, "Roman amphitheatre", 12.4922, 41.8902)));
        var provider = new WikidataAttractionProvider(http.Client(), TestDoubles.Cache(), OpenTripMapPlaceProviderTests.Attractions(http, openTripMapKey: ""));

        var place = Assert.Single(await provider.SearchAsync(Rome, 3, CancellationToken.None));

        Assert.Equal("Colosseum", place.Name);
    }

    [Fact]
    public async Task Landmarks_are_cached_per_city()
    {
        var http = new StubHttp().On("query.wikidata.org", Landmarks);
        var provider = Provider(http, TestDoubles.Cache());

        await provider.SearchAsync(Rome, 3, CancellationToken.None);
        await provider.SearchAsync(Rome, 2, CancellationToken.None);

        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task Details_combine_wikidata_with_the_wikipedia_summary()
    {
        var http = new StubHttp()
            .On("query.wikidata.org", Bindings(
                """{"label":{"value":"Colosseum"},"coord":{"value":"Point(12.4922 41.8902)"},"typeLabel":{"value":"Roman amphitheatre"},"article":{"value":"https://en.wikipedia.org/wiki/Colosseum"}}"""))
            .On("wikipedia.org/api/rest_v1/page/summary/Colosseum", """
                {"extract":"The Colosseum is an elliptical amphitheatre.","thumbnail":{"source":"https://upload/colosseum.jpg"}}
                """);

        var place = await Provider(http).GetAsync("Q10285", CancellationToken.None);

        Assert.NotNull(place);
        Assert.Equal("wd:Q10285", place.Id);
        Assert.Equal("Colosseum", place.Name);
        Assert.Equal("Roman amphitheatre", place.Kind);
        Assert.Equal(new GeoPoint(41.8902, 12.4922), place.Location);
        Assert.Equal("The Colosseum is an elliptical amphitheatre.", place.Description);
        Assert.Equal("https://upload/colosseum.jpg", place.ImageUrl);
        Assert.Equal("https://en.wikipedia.org/wiki/Colosseum", place.Website); // no official site: the article
    }

    [Fact]
    public async Task Details_prefer_the_wikidata_photo_and_the_official_website()
    {
        var http = new StubHttp()
            .On("query.wikidata.org", Bindings(
                """
                {"label":{"value":"Eiffel Tower"},"typeLabel":{"value":"observation tower"},"website":{"value":"https://www.toureiffel.paris"},
                 "image":{"value":"http://commons.wikimedia.org/wiki/Special:FilePath/Tour%20Eiffel.jpg"},"article":{"value":"https://en.wikipedia.org/wiki/Eiffel_Tower"}}
                """))
            .On("wikipedia.org", """{"extract":"A lattice tower.","thumbnail":{"source":"https://upload/eiffel-logo.png"}}""");

        var place = await Provider(http).GetAsync("Q243", CancellationToken.None);

        Assert.NotNull(place);
        Assert.Equal("http://commons.wikimedia.org/wiki/Special:FilePath/Tour%20Eiffel.jpg?width=800", place.ImageUrl);
        Assert.Equal("https://www.toureiffel.paris", place.Website);
    }

    [Fact]
    public async Task Unknown_place_returns_null()
    {
        var http = new StubHttp().On("query.wikidata.org", Bindings());

        Assert.Null(await Provider(http).GetAsync("Q999999999", CancellationToken.None));
    }

    [Theory]
    [InlineData("Q1 } DELETE")]
    [InlineData("P31")]
    [InlineData("")]
    public async Task Ids_that_are_not_wikidata_ids_are_rejected_without_a_query(string id)
    {
        var http = new StubHttp();

        Assert.Null(await Provider(http).GetAsync(id, CancellationToken.None));
        Assert.Empty(http.Requests);
    }

    private static WikidataAttractionProvider Provider(StubHttp http, JsonCache? cache = null) =>
        new(http.Client(), cache ?? TestDoubles.Cache(), OpenTripMapPlaceProviderTests.Attractions(http));

    private static string Bindings(params string[] rows) => "{\"results\":{\"bindings\":[" + string.Join(',', rows) + "]}}";

    private static string Row(string id, string? name, int articles, string type, double lng, double lat, string? extra = null)
    {
        var fields = new List<string>
        {
            $"\"item\":{{\"value\":\"http://www.wikidata.org/entity/{id}\"}}",
            $"\"articles\":{{\"value\":\"{articles.ToString(CultureInfo.InvariantCulture)}\"}}",
            string.Create(CultureInfo.InvariantCulture, $"\"coord\":{{\"value\":\"Point({lng} {lat})\"}}"),
            $"\"typeLabel\":{{\"value\":\"{type}\"}}",
        };
        if (name is not null)
        {
            fields.Add($"\"label\":{{\"value\":\"{name}\"}}");
        }

        if (extra is not null)
        {
            fields.Add(extra);
        }

        return "{" + string.Join(',', fields) + "}";
    }
}
