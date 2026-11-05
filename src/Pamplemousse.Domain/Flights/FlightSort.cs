using Pamplemousse.Domain.Exceptions;

namespace Pamplemousse.Domain.Flights;

/// <summary>Order of a flight list: cheapest first (the source's order), or best match for the preferences (spec 007).</summary>
public enum FlightSort
{
    Cheapest,
    Match,
}

public static class FlightSorts
{
    /// <summary>Null or empty is <see cref="FlightSort.Cheapest"/>; any case is accepted.</summary>
    public static FlightSort Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return FlightSort.Cheapest;
        }

        return Enum.TryParse<FlightSort>(value.Trim(), ignoreCase: true, out var sort) && Enum.IsDefined(sort)
            ? sort
            : throw new DomainException("Use 'cheapest' or 'match'.", "sort");
    }
}
