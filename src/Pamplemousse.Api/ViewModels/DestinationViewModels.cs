using Pamplemousse.Domain.Destinations;

namespace Pamplemousse.Api.ViewModels;

public sealed record DestinationSummaryViewModel(
    string Id,
    string Name,
    string Region,
    string CountryCode,
    string CountryName,
    string? Flag,
    double Lat,
    double Lng,
    long Population,
    string TimeZone);

public sealed record DestinationDetailsViewModel(DestinationSummaryViewModel Destination, CountryViewModel? Country);

public sealed record CountryViewModel(
    string Code,
    string Name,
    string OfficialName,
    string? Capital,
    string? Region,
    string? Subregion,
    IReadOnlyList<CurrencyViewModel> Currencies,
    IReadOnlyList<string> Languages,
    string? CallingCode,
    string? Flag);

public sealed record CurrencyViewModel(string Code, string? Name, string? Symbol);

public sealed record CountryOptionViewModel(string Code, string Name, string? Flag, IReadOnlyList<string> Currencies);

public sealed record DateRangeViewModel(DateOnly From, DateOnly To);

public sealed record OverviewViewModel(
    DestinationSummaryViewModel Destination,
    CountryViewModel? Country,
    DateRangeViewModel Dates,
    WeatherReportViewModel? Weather,
    ExchangeRateViewModel? Exchange,
    IReadOnlyList<HolidayViewModel>? Holidays,
    VisaRequirementViewModel? Visa,
    TravelAdvisoryViewModel? Advisory,
    IReadOnlyList<string> Warnings);

public sealed record WeatherReportViewModel(WeatherKind Kind, IReadOnlyList<WeatherDayViewModel> Days);

public sealed record WeatherDayViewModel(DateOnly Date, double MaxC, double MinC, int? RainChance, int Code, string Summary);

public sealed record ExchangeRateViewModel(string Base, string Quote, decimal Rate, DateOnly Date, string Source);

public sealed record HolidayViewModel(DateOnly Date, string Name, string LocalName);

public sealed record VisaRequirementViewModel(string Passport, string Destination, string Requirement, int? VisaFreeDays);

public sealed record TravelAdvisoryViewModel(int Level, string Label, string Summary, string SourceUrl);
