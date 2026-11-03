using System.Globalization;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Exceptions;

namespace Pamplemousse.Domain.Hotels;

/// <summary>What the traveller asked for: one room for adults only. Only <see cref="Create"/> makes one, so every instance is valid.</summary>
public sealed record HotelSearch
{
    public const string DefaultCurrency = "USD";
    public const int DefaultAdults = 2;
    public const int MaxAdults = 8;

    private HotelSearch(string destinationId, DateRange dates, int adults, string currency)
    {
        DestinationId = destinationId;
        Dates = dates;
        Adults = adults;
        Currency = currency;
    }

    public string DestinationId { get; }

    /// <summary>Check-in is <see cref="DateRange.From"/>, check-out is <see cref="DateRange.To"/>.</summary>
    public DateRange Dates { get; }

    public int Adults { get; }

    public string Currency { get; }

    public int Nights => Dates.To.DayNumber - Dates.From.DayNumber;

    /// <summary>Identifies the search for caching and price history. Sorting, filters and page are not part of it.</summary>
    public string Key => string.Create(CultureInfo.InvariantCulture, $"{DestinationId}:{Dates.From:yyyyMMdd}:{Dates.To:yyyyMMdd}:{Adults}:{Currency}");

    public static HotelSearch Create(string destinationId, DateRange dates, int? adults, string? currency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationId);
        ArgumentNullException.ThrowIfNull(dates);

        // A hotel night needs a check-out after the check-in, unlike a same-day trip.
        if (dates.To <= dates.From)
        {
            throw new DomainException("Check-out ('to') must be after check-in ('from').", "to");
        }

        var count = adults ?? DefaultAdults;
        if (count is < 1 or > MaxAdults)
        {
            throw new DomainException($"Adults must be between 1 and {MaxAdults}.", "adults");
        }

        var code = string.IsNullOrWhiteSpace(currency) ? DefaultCurrency : currency.Trim().ToUpperInvariant();
        if (code.Length != 3 || !code.All(char.IsAsciiLetter))
        {
            throw new DomainException("Currency must be a 3-letter code, such as USD.", "currency");
        }

        return new HotelSearch(destinationId, dates, count, code);
    }
}
