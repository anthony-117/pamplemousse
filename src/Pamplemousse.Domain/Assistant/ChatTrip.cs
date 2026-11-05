using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Exceptions;

namespace Pamplemousse.Domain.Assistant;

/// <summary>
/// The trip the chat is about, used as the default for every tool call (spec 003, FR-006). Every value is optional:
/// with no destination the assistant finds cities itself. Only <see cref="Create"/> makes one.
/// </summary>
public sealed record ChatTrip
{
    public const string DefaultPassport = "LB";
    public const string DefaultCurrency = "USD";
    public const int DefaultAdults = 2;
    public const int MaxAdults = 8;

    private ChatTrip(string? destinationId, DateRange dates, int adults, string passport, string currency, string? originId)
    {
        DestinationId = destinationId;
        Dates = dates;
        Adults = adults;
        Passport = passport;
        Currency = currency;
        OriginId = originId;
    }

    public string? DestinationId { get; }

    public DateRange Dates { get; }

    public int Adults { get; }

    public string Passport { get; }

    public string Currency { get; }

    /// <summary>Departure city for flights, when the traveller chose one.</summary>
    public string? OriginId { get; }

    public static ChatTrip Create(
        string? destinationId, DateOnly? from, DateOnly? to, int? adults, string? passport, string? currency, string? originId, DateOnly today)
    {
        var dates = DateRange.ForTrip(from, to, today);

        var count = adults ?? DefaultAdults;
        if (count is < 1 or > MaxAdults)
        {
            throw new DomainException($"Adults must be between 1 and {MaxAdults}.", "context.adults");
        }

        return new ChatTrip(
            Blank(destinationId),
            dates,
            count,
            Code(passport, 2, DefaultPassport, "context.passport"),
            Code(currency, 3, DefaultCurrency, "context.currency"),
            Blank(originId));
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Code(string? value, int length, string fallback, string field)
    {
        var code = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToUpperInvariant();
        if (code.Length != length || !code.All(char.IsAsciiLetter))
        {
            throw new DomainException($"'{field}' must be a {length}-letter code.", field);
        }

        return code;
    }
}
