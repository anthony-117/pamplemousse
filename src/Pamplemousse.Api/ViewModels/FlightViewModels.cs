using Pamplemousse.Domain.Flights;

namespace Pamplemousse.Api.ViewModels;

public sealed record FlightSearchViewModel(
    FlightEndpointViewModel Origin,
    FlightEndpointViewModel Destination,
    DateRangeViewModel Dates,
    TripType TripType,
    bool DirectOnly,
    string Currency,
    IReadOnlyList<FlightViewModel> Items,
    string PriceNote,
    string? Message,
    string? SearchUrl,
    IReadOnlyList<string> Warnings,
    FlightSort Sort,
    WeightsViewModel? Weights,
    decimal? TripBudget,
    string? RankingNote);

public sealed record FlightEndpointViewModel(string Id, string Name, string? FlightCity, string? Code, bool IsNearby, double? DistanceKm);

public sealed record FlightViewModel(
    string Id,
    string Airline,
    string? FlightNumber,
    string OriginAirport,
    string DestinationAirport,
    DateTimeOffset DepartureAt,
    DateTimeOffset? ReturnAt,
    int Stops,
    int? ReturnStops,
    int? DurationMinutes,
    MoneyViewModel Price,
    string BookingUrl,
    DateMatch DateMatch,
    string Source,
    MatchViewModel? Match);
