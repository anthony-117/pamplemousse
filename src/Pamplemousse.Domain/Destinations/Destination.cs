using Pamplemousse.Domain.Common;

namespace Pamplemousse.Domain.Destinations;

/// <summary>A city travellers can pick (GeoNames city with 15,000+ people). <see cref="Id"/> is the GeoNames id.</summary>
public sealed record Destination(
    string Id,
    string Name,
    string Region,
    string CountryCode,
    GeoPoint Location,
    long Population,
    string TimeZone);

public sealed record Country(
    string Code,
    string Name,
    string OfficialName,
    string? Capital,
    string? Region,
    string? Subregion,
    IReadOnlyList<CurrencyInfo> Currencies,
    IReadOnlyList<string> Languages,
    string? CallingCode,
    string? Flag);

public sealed record CurrencyInfo(string Code, string? Name, string? Symbol);
