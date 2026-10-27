using Pamplemousse.Domain.Abstractions;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Infrastructure.Providers;

namespace Pamplemousse.Infrastructure.Tests.Providers;

public class TicketmasterEventProviderTests
{
    private static readonly Destination Rome = new("3169070", "Rome", "Lazio", "IT", new GeoPoint(41.8919, 12.5113), 2_318_895, "Europe/Rome");
    private static readonly DateOnly From = new(2026, 11, 26);
    private static readonly DateOnly To = new(2026, 11, 30);

    [Fact]
    public async Task Parses_events_with_venue_time_price_and_largest_image()
    {
        var http = new StubHttp().On("events.json", """
            {"_embedded":{"events":[
              {"id":"E1","name":"AS Roma vs Lazio","url":"https://tm/e1",
               "dates":{"start":{"localDate":"2026-11-28","localTime":"20:45:00"}},
               "_embedded":{"venues":[{"name":"Stadio Olimpico"}]},
               "priceRanges":[{"min":45.0,"currency":"EUR"}],
               "classifications":[{"segment":{"name":"Sports"}}],
               "images":[{"url":"https://img/small.jpg","width":100},{"url":"https://img/big.jpg","width":1024}]},
              {"id":"E2","name":"No date","url":"https://tm/e2","dates":{"start":{}}}]}}
            """);
        var provider = new TicketmasterEventProvider(http.Client(), TestDoubles.Cache(), TestDoubles.Keys());

        var events = await provider.SearchAsync(Rome, From, To, 10, CancellationToken.None);

        var e = Assert.Single(events); // the event without a date is skipped
        Assert.Equal("tm:E1", e.Id);
        Assert.Equal(new DateOnly(2026, 11, 28), e.Date);
        Assert.Equal("20:45", e.Time);
        Assert.Equal("Stadio Olimpico", e.Venue);
        Assert.Equal("Sports", e.Category);
        Assert.Equal(new Money(45m, "EUR"), e.PriceFrom);
        Assert.Equal("https://img/big.jpg", e.ImageUrl);
        Assert.Contains("startDateTime=2026-11-26T00:00:00Z&endDateTime=2026-11-30T23:59:59Z", http.Requests[0].Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_same_show_listed_twice_appears_once_but_other_dates_stay()
    {
        static string Show(string id, string date) =>
            "{\"id\":\"" + id + "\",\"name\":\"SHIVA - Tour\",\"url\":\"https://tm/s\"," +
            "\"_embedded\":{\"venues\":[{\"name\":\"Palazzo dello Sport\"}]},\"classifications\":[{\"segment\":{\"name\":\"Music\"}}]," +
            "\"dates\":{\"start\":{\"localDate\":\"" + date + "\",\"localTime\":\"21:00:00\"}}}";
        var http = new StubHttp().On(
            "events.json",
            "{\"_embedded\":{\"events\":[" + Show("A", "2026-11-27") + "," + Show("B", "2026-11-27") + "," + Show("C", "2026-11-29") + "]}}");
        var provider = new TicketmasterEventProvider(http.Client(), TestDoubles.Cache(), TestDoubles.Keys());

        var events = await provider.SearchAsync(Rome, From, To, 10, CancellationToken.None);

        Assert.Equal(["tm:A", "tm:C"], events.Select(e => e.Id), StringComparer.Ordinal);
    }

    [Fact]
    public async Task No_events_means_an_empty_list()
    {
        var http = new StubHttp().On("events.json", """{"page":{"totalElements":0}}""");
        var provider = new TicketmasterEventProvider(http.Client(), TestDoubles.Cache(), TestDoubles.Keys());

        Assert.Empty(await provider.SearchAsync(Rome, From, To, 10, CancellationToken.None));
    }

    [Fact]
    public async Task Missing_key_is_reported_without_calling_the_api()
    {
        var http = new StubHttp();
        var provider = new TicketmasterEventProvider(http.Client(), TestDoubles.Cache(), TestDoubles.Keys(ticketmaster: ""));

        var error = await Assert.ThrowsAsync<ProviderNotConfiguredException>(() => provider.SearchAsync(Rome, From, To, 10, CancellationToken.None));

        Assert.Equal("TICKETMASTER_API_KEY", error.EnvironmentVariable);
        Assert.Empty(http.Requests);
    }
}
