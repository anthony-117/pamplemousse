using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Exceptions;

namespace Pamplemousse.Domain.Flights;

/// <summary>What the traveller asked for. Only <see cref="Create"/> makes one, so every instance is valid.</summary>
public sealed record FlightSearch
{
    public const string DefaultCurrency = "USD";

    private FlightSearch(string originCode, string destinationCode, DateRange dates, TripType tripType, bool directOnly, string currency)
    {
        OriginCode = originCode;
        DestinationCode = destinationCode;
        Dates = dates;
        TripType = tripType;
        DirectOnly = directOnly;
        Currency = currency;
    }

    public string OriginCode { get; }

    public string DestinationCode { get; }

    public DateRange Dates { get; }

    public TripType TripType { get; }

    public bool DirectOnly { get; }

    public string Currency { get; }

    public static FlightSearch Create(string originCode, string destinationCode, DateRange dates, TripType tripType, bool directOnly, string? currency)
    {
        // Two different cities can share a flight city: Byblos and Beirut both fly from BEY.
        if (string.Equals(originCode, destinationCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException($"The departure city and the destination both use the same flight city ({destinationCode}).", "origin");
        }

        var code = string.IsNullOrWhiteSpace(currency) ? DefaultCurrency : currency.Trim().ToUpperInvariant();
        if (code.Length != 3 || !code.All(char.IsAsciiLetter))
        {
            throw new DomainException("Currency must be a 3-letter code, such as USD.", "currency");
        }

        return new FlightSearch(originCode, destinationCode, dates, tripType, directOnly, code);
    }
}
