namespace Pamplemousse.Domain.Destinations;

public enum WeatherKind
{
    /// <summary>Live forecast (available up to 16 days ahead).</summary>
    Forecast,

    /// <summary>Observed weather on the same dates last year, for trips further ahead.</summary>
    LastYear,
}

public sealed record WeatherReport(WeatherKind Kind, IReadOnlyList<WeatherDay> Days);

public sealed record WeatherDay(DateOnly Date, double MaxC, double MinC, int? RainChance, int Code, string Summary);

public sealed record ExchangeRate(string Base, string Quote, decimal Rate, DateOnly Date, string Source);

public sealed record Holiday(DateOnly Date, string Name, string LocalName);

/// <summary>
/// Entry rule for a passport (ISO country code) visiting a destination country.
/// <c>Requirement</c> is "visa required", "e-visa", "visa on arrival", "eta", "visa free" or "own country";
/// <c>VisaFreeDays</c> is the number of days allowed without a visa, when the source gives one.
/// </summary>
public sealed record VisaRequirement(string Passport, string Destination, string Requirement, int? VisaFreeDays);

/// <summary>
/// US State Department advisory: <c>Level</c> 1 (normal precautions) to 4 (do not travel), its label, a short summary and the source page.
/// </summary>
public sealed record TravelAdvisory(int Level, string Label, string Summary, string SourceUrl);
