using System.Text.Json.Nodes;
using Pamplemousse.Api.ViewModels;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Cars.Queries.SearchCars;
using Pamplemousse.Application.Destinations.Queries.GetOverview;
using Pamplemousse.Application.Flights.Queries.SearchFlights;
using Pamplemousse.Application.Hotels.Queries.SearchHotels;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Domain.Itinerary;
using Pamplemousse.Domain.Places;
using Pamplemousse.Domain.Saved;
using Pamplemousse.Domain.TravelEvents;

namespace Pamplemousse.Api.Mappings;

/// <summary>
/// The cards of contracts/chat-api.md, written with the list endpoints' own mappings (research R-4): a card's item is
/// byte for byte what its list returns for the same search.
/// </summary>
internal sealed class ApiCardJson : ICardJson
{
    public IReadOnlyList<string> Hotels(SearchHotelsResponse response, int take, CardCity city, DateTimeOffset foundAt) =>
        [.. response.ToViewModel().Items.Take(take).Select(h =>
            Write(CardJson.Envelope("hotel", h.Id, h.Location.Lat, h.Location.Lng, false, foundAt, city.Id, city.Name, h)))];

    /// <summary>At the arrival city's centre: the bundled flight cities have no airport positions (spec 010 FR-005).</summary>
    public IReadOnlyList<string> Flights(SearchFlightsResponse response, int take, CardCity city, DateTimeOffset foundAt) =>
        [.. response.ToViewModel().Items.Take(take).Select(f =>
            Write(CardJson.Envelope("flight", f.Id, city.Centre.Lat, city.Centre.Lng, true, foundAt, city.Id, city.Name, f)))];

    public IReadOnlyList<string> Cars(SearchCarsResponse response, int take, CardCity city, DateTimeOffset foundAt) =>
        [.. response.ToViewModel().Items.Take(take).Select(c =>
            Write(CardJson.Envelope("car", c.Id, c.PickUp.Location.Lat, c.PickUp.Location.Lng, false, foundAt, city.Id, city.Name, c)))];

    public IReadOnlyList<string> Places(IReadOnlyList<Place> places, CardCity city, DateTimeOffset foundAt) =>
        [.. places.Select(p => Write(CardJson.Of(p, city.Id, city.Name, foundAt)))];

    public IReadOnlyList<string> Events(IReadOnlyList<TravelEvent> events, CardCity city, DateTimeOffset foundAt) =>
        [.. events.Select(e => Write(CardJson.Envelope("event", e.Id, city.Centre.Lat, city.Centre.Lng, true, foundAt, city.Id, city.Name, e.ToViewModel())))];

    /// <summary>The overview's parts in the facts shape the app reads; each part null when its source had nothing.</summary>
    public string Facts(OverviewResponse overview, CardCity city, DateTimeOffset foundAt)
    {
        ArgumentNullException.ThrowIfNull(overview);
        ArgumentNullException.ThrowIfNull(city);
        var days = overview.Weather?.Days ?? [];
        var item = new
        {
            destination = new { id = overview.Destination.Id, name = overview.Destination.Name, countryCode = overview.Destination.CountryCode },
            from = overview.Dates.From,
            to = overview.Dates.To,
            weather = days.Count == 0 ? null : new
            {
                kind = overview.Weather!.Kind == WeatherKind.Forecast ? "forecast" : "climate",
                summary = days.GroupBy(d => d.Summary, StringComparer.Ordinal).OrderByDescending(g => g.Count()).First().Key,
                maxC = days.Max(d => d.MaxC),
                minC = days.Min(d => d.MinC),
                rainChance = days.Max(d => d.RainChance),
            },
            visa = overview.Visa is { } v ? new VisaRequirementViewModel(v.Passport, v.Destination, v.Requirement, v.VisaFreeDays) : null,
            currency = overview.Exchange is { } x ? new { from = x.Base, to = x.Quote, rate = x.Rate, date = x.Date } : null,
            safety = overview.Advisory is { } a ? new TravelAdvisoryViewModel(a.Level, a.Label, a.Summary, a.SourceUrl) : null,
            holidays = (overview.Holidays ?? []).Select(h => new HolidayViewModel(h.Date, h.Name, h.LocalName)).ToList(),
        };
        return Write(CardJson.Envelope("facts", overview.Destination.Id, city.Centre.Lat, city.Centre.Lng, true, foundAt, city.Id, city.Name, item));
    }

    public string Plan(Plan plan, DateTimeOffset foundAt)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var city = plan.Request.Destination;
        var item = PlanSummary.Of(plan).ToViewModel();
        return Write(CardJson.Envelope("plan", plan.Id, city.Centre.Lat, city.Centre.Lng, false, foundAt, city.Id, city.Name, item));
    }

    public IReadOnlyList<string> PlanOffers(Plan plan, OfferKind kind, int take, DateTimeOffset foundAt)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var city = plan.Request.Destination;
        var at = city.Centre;
        return plan.AlternativesOf(kind) switch
        {
            PlanAlternativesViewModel<HotelViewModel> h => [.. h.Items.Take(take).Select(x =>
                Write(CardJson.Envelope("hotel", x.Id, x.Location.Lat, x.Location.Lng, false, h.CheckedAt ?? foundAt, city.Id, city.Name, x)))],
            PlanAlternativesViewModel<FlightViewModel> f => [.. f.Items.Take(take).Select(x =>
                Write(CardJson.Envelope("flight", x.Id, at.Lat, at.Lng, true, f.CheckedAt ?? foundAt, city.Id, city.Name, x)))],
            PlanAlternativesViewModel<CarViewModel> c => [.. c.Items.Take(take).Select(x =>
                Write(CardJson.Envelope("car", x.Id, x.PickUp.Location.Lat, x.PickUp.Location.Lng, false, c.CheckedAt ?? foundAt, city.Id, city.Name, x)))],
            _ => [],
        };
    }

    public IReadOnlyList<string> Saved(IReadOnlyList<SavedItem> items) =>
        [.. items.Where(i => i.ItemJson is not null).Select(i => Write(CardJson.Envelope(
            i.Kind.ToString().ToLowerInvariant(), i.Id, i.Location.Lat, i.Location.Lng, false, i.SavedAt, i.DestinationId, i.DestinationName, JsonNode.Parse(i.ItemJson!)!)))];

    private static string Write(JsonObject card) => card.ToJsonString(CardJson.Options);
}
