using Pamplemousse.Api.ViewModels;
using Pamplemousse.Application.Flights.Queries.SearchFlights;
using Pamplemousse.Domain.Flights;

namespace Pamplemousse.Api.Mappings;

internal static class FlightMappings
{
    public static FlightSearchViewModel ToViewModel(this SearchFlightsResponse r) => new(
        r.Origin.ToViewModel(),
        r.Destination.ToViewModel(),
        new DateRangeViewModel(r.Dates.From, r.Dates.To),
        r.TripType,
        r.DirectOnly,
        r.Currency,
        [.. r.Items.Select(ToViewModel)],
        r.PriceNote,
        r.Message,
        r.SearchUrl,
        r.Warnings);

    private static FlightEndpointViewModel ToViewModel(this FlightEndpoint e) => new(e.Id, e.Name, e.FlightCity, e.Code, e.IsNearby, e.DistanceKm);

    private static FlightViewModel ToViewModel(this FlightOffer f) => new(
        f.Id,
        f.Airline,
        f.FlightNumber,
        f.OriginAirport,
        f.DestinationAirport,
        f.DepartureAt,
        f.ReturnAt,
        f.Stops,
        f.ReturnStops,
        f.DurationMinutes,
        new MoneyViewModel(f.Price.Amount, f.Price.Currency),
        f.BookingUrl,
        f.DateMatch,
        f.Source);
}
