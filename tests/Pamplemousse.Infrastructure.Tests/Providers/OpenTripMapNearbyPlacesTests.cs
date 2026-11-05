using System.Net;
using System.Text.Json;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Common;
using Pamplemousse.Infrastructure.Providers;

namespace Pamplemousse.Infrastructure.Tests.Providers;

public class OpenTripMapNearbyPlacesTests
{
    private static readonly GeoPoint ViaNazionale = new(41.9009, 12.4932);

    // Trimmed from the 5 Nov answers around Via Nazionale: a café-bar counts as both food and nightlife.
    private const string FoodAndNight = """
        [{"xid":"N1","name":"La Cucina Nazionale","kinds":"foods,restaurants,tourist_facilities","dist":120},
         {"xid":"N2","name":"Tazio","kinds":"foods,cafes,tourist_facilities","dist":180},
         {"xid":"N3","name":"Apotheke Cocktail Bar","kinds":"foods,bars,tourist_facilities","dist":300},
         {"xid":"N4","name":"Caffè e Bar","kinds":"foods,cafes,bars","dist":420},
         {"xid":"N5","name":"","kinds":"foods,pubs","dist":900}]
        """;

    private const string Nature = """
        [{"xid":"W1","name":"Giardini di Palazzo Barberini","kinds":"urban_environment,gardens_and_parks,interesting_places","dist":700},
         {"xid":"W2","name":"Horti Sallustiani","kinds":"natural,interesting_places","dist":1500}]
        """;

    private static OpenTripMapNearbyPlaces Places(StubHttp http, string key = "otm-key") => new(http.Client(), TestDoubles.Cache(), TestDoubles.Keys(openTripMap: key));

    private static StubHttp Both() => new StubHttp()
        .On("radius=1000", FoodAndNight)
        .On("radius=2000", Nature);

    [Fact]
    public async Task Counts_food_nightlife_and_nature_by_kind()
    {
        var counts = await Places(Both()).CountAsync(ViaNazionale, CancellationToken.None);

        Assert.Equal(new NearbyCounts(Food: 3, Nightlife: 3, Nature: 2), counts);
    }

    [Fact]
    public async Task Asks_for_the_right_kinds_and_distances()
    {
        var http = Both();

        await Places(http).CountAsync(ViaNazionale, CancellationToken.None);

        Assert.Equal(2, http.Requests.Count);
        Assert.Contains("radius=1000&lon=12.4932&lat=41.9009&kinds=restaurants,cafes,bars,pubs,nightclubs&format=json&limit=500&apikey=otm-key", http.Requests[0].Query, StringComparison.Ordinal);
        Assert.Contains("radius=2000&lon=12.4932&lat=41.9009&kinds=natural,gardens_and_parks", http.Requests[1].Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_same_position_is_asked_once()
    {
        var http = Both();
        var places = Places(http);

        await places.CountAsync(ViaNazionale, CancellationToken.None);
        await places.CountAsync(ViaNazionale, CancellationToken.None);

        Assert.Equal(2, http.Requests.Count);
    }

    [Fact]
    public async Task No_key_says_which_variable_is_missing()
    {
        var ex = await Assert.ThrowsAsync<ProviderNotConfiguredException>(() => Places(Both(), key: "").CountAsync(ViaNazionale, CancellationToken.None));

        Assert.Contains("OPENTRIPMAP_API_KEY", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refused_request_fails_so_the_caller_can_count_it_as_average()
    {
        var http = new StubHttp().On("radius=", "{\"error\":\"limit\"}", HttpStatusCode.TooManyRequests);

        await Assert.ThrowsAsync<HttpRequestException>(() => Places(http).CountAsync(ViaNazionale, CancellationToken.None));
    }

    [Fact]
    public async Task A_broken_answer_fails()
    {
        var http = new StubHttp().On("radius=", "not json");

        await Assert.ThrowsAnyAsync<JsonException>(() => Places(http).CountAsync(ViaNazionale, CancellationToken.None));
    }
}
