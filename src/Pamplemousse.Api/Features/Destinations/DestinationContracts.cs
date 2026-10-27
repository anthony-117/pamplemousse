using Pamplemousse.Domain.Destinations;
using Pamplemousse.Domain.Events;
using Pamplemousse.Domain.Places;

namespace Pamplemousse.Api.Features.Destinations;

/// <summary>A search result or destination header: the city plus its country name and flag.</summary>
public sealed record DestinationSummary(
    string Id,
    string Name,
    string Region,
    string CountryCode,
    string CountryName,
    string? Flag,
    double Lat,
    double Lng,
    long Population,
    string TimeZone)
{
    public static DestinationSummary From(Destination d, Country? c) => new(
        d.Id, d.Name, d.Region, d.CountryCode, c?.Name ?? d.CountryCode, c?.Flag,
        d.Location.Lat, d.Location.Lng, d.Population, d.TimeZone);
}

public sealed record DestinationDetails(DestinationSummary Destination, Country? Country);

public sealed record TripDates(DateOnly From, DateOnly To);

/// <summary>
/// Everything about a city for given dates. Each section is independent: when a source fails or is not configured
/// the section is null and <see cref="Warnings"/> says why, so the page still loads.
/// </summary>
public sealed record OverviewResponse(
    DestinationSummary Destination,
    Country? Country,
    TripDates Dates,
    WeatherReport? Weather,
    ExchangeRate? Exchange,
    IReadOnlyList<Holiday>? Holidays,
    VisaRequirement? Visa,
    TravelAdvisory? Advisory,
    IReadOnlyList<string> Warnings);

public sealed record PlacesResponse(PlaceCategory Category, IReadOnlyList<Place> Items, IReadOnlyList<string> Warnings);

public sealed record EventsResponse(TripDates Dates, IReadOnlyList<TravelEvent> Items, IReadOnlyList<string> Warnings);

public sealed record CountryOption(string Code, string Name, string? Flag, IReadOnlyList<string> Currencies);
