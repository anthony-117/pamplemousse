using Pamplemousse.Domain.Exceptions;

namespace Pamplemousse.Domain.Common;

/// <summary>The dates of a trip. Only <see cref="ForTrip"/> creates one, so every instance respects the rules.</summary>
public sealed record DateRange
{
    public const int MaxTripDays = 30;

    private DateRange(DateOnly from, DateOnly to)
    {
        From = from;
        To = to;
    }

    public DateOnly From { get; }

    public DateOnly To { get; }

    /// <summary>Defaults to a 5-day trip starting in 30 days; rejects ranges that end before they start, exceed 30 days or are in the past.</summary>
    public static DateRange ForTrip(DateOnly? from, DateOnly? to, DateOnly today)
    {
        var start = from ?? today.AddDays(30);
        var end = to ?? start.AddDays(4);
        if (end < start || end.DayNumber - start.DayNumber > MaxTripDays || start < today.AddDays(-1))
        {
            throw new DomainException($"Dates must be in the future, 'to' on or after 'from', and at most {MaxTripDays} days apart.", "to");
        }

        return new DateRange(start, end);
    }
}
