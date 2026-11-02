using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Flights;

namespace Pamplemousse.Api.Tests;

/// <summary>Stands in for the real flight source, so endpoint tests never call the internet.</summary>
public sealed class FakeFlights : IFlightProvider
{
    public Exception? Error { get; set; }

    public IReadOnlyList<FlightOffer> Offers { get; set; } = [Offer("tp:cheap", 90), Offer("tp:dear", 240)];

    public FlightSearch? LastSearch { get; private set; }

    public static FlightOffer Offer(string id, decimal price, DateMatch match = DateMatch.Exact) => new(
        id,
        "Air France",
        "AF1404",
        "CDG",
        "FCO",
        new DateTimeOffset(2027, 4, 10, 9, 25, 0, TimeSpan.FromHours(2)),
        new DateTimeOffset(2027, 4, 14, 18, 10, 0, TimeSpan.FromHours(2)),
        0,
        0,
        260,
        new Money(price, "USD"),
        "https://www.aviasales.com/search/PAR1004ROM14041?marker=1",
        match,
        "Travelpayouts");

    public Task<FlightResults> SearchAsync(FlightSearch search, CancellationToken ct)
    {
        LastSearch = search;
        return Error is null
            ? Task.FromResult(new FlightResults(Offers, "https://www.aviasales.com/search/PAR1004ROM14041", []))
            : Task.FromException<FlightResults>(Error);
    }
}
