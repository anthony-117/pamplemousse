using Pamplemousse.Domain.Destinations;

namespace Pamplemousse.Application.Destinations;

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
    internal static DestinationSummary From(Destination d, Country? c) => new(
        d.Id, d.Name, d.Region, d.CountryCode, c?.Name ?? d.CountryCode, c?.Flag,
        d.Location.Lat, d.Location.Lng, d.Population, d.TimeZone);
}
