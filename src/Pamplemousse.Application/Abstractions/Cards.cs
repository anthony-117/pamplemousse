using Pamplemousse.Application.Cars.Queries.SearchCars;
using Pamplemousse.Application.Destinations.Queries.GetOverview;
using Pamplemousse.Application.Flights.Queries.SearchFlights;
using Pamplemousse.Application.Hotels.Queries.SearchHotels;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Itinerary;
using Pamplemousse.Domain.Places;
using Pamplemousse.Domain.Saved;
using Pamplemousse.Domain.TravelEvents;

namespace Pamplemousse.Application.Abstractions;

/// <summary>The city a card's item is in: its id and name go in the envelope, its centre is the position of an item without one.</summary>
public sealed record CardCity(string Id, string Name, GeoPoint Centre);

/// <summary>
/// Writes cards as the app receives them (spec 010, contracts/chat-api.md "Cards", research R-4): one envelope around the
/// item exactly as its list endpoint writes it. The Api implements it with its list mappings, so the shapes never drift.
/// Each list method writes at most <c>take</c> cards, best first.
/// </summary>
public interface ICardJson
{
    IReadOnlyList<string> Hotels(SearchHotelsResponse response, int take, CardCity city, DateTimeOffset foundAt);

    IReadOnlyList<string> Flights(SearchFlightsResponse response, int take, CardCity city, DateTimeOffset foundAt);

    IReadOnlyList<string> Cars(SearchCarsResponse response, int take, CardCity city, DateTimeOffset foundAt);

    IReadOnlyList<string> Places(IReadOnlyList<Place> places, CardCity city, DateTimeOffset foundAt);

    IReadOnlyList<string> Events(IReadOnlyList<TravelEvent> events, CardCity city, DateTimeOffset foundAt);

    string Facts(OverviewResponse overview, CardCity city, DateTimeOffset foundAt);

    string Plan(Plan plan, DateTimeOffset foundAt);

    /// <summary>The kept hotels, flights or cars of a plan, best first.</summary>
    IReadOnlyList<string> PlanOffers(Plan plan, OfferKind kind, int take, DateTimeOffset foundAt);

    /// <summary>Saved items with their stored item; an item saved without one has no card.</summary>
    IReadOnlyList<string> Saved(IReadOnlyList<SavedItem> items);
}
