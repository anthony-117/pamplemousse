using System.Globalization;

namespace Pamplemousse.Domain.Ranking;

/// <summary>Prices and counts as the breakdown and reasons show them, the same on every machine (FR-009).</summary>
public static class PriceText
{
    /// <summary>"$612", "€1,240", "1,500 AED"; whole units, as the app shows them. A null currency means USD.</summary>
    public static string Format(decimal amount, string? currency)
    {
        var whole = Math.Round(amount, 0, MidpointRounding.AwayFromZero).ToString("N0", CultureInfo.InvariantCulture);
        return (currency ?? "USD").ToUpperInvariant() switch
        {
            "USD" => "$" + whole,
            "EUR" => "€" + whole,
            "GBP" => "£" + whole,
            var code => $"{whole} {code}",
        };
    }

    /// <summary>"the cheapest of 20", "the 4th cheapest of 20".</summary>
    public static string CheapestRank(int position, int count) =>
        position <= 1 ? $"the cheapest of {count}" : $"the {Ordinal(position)} cheapest of {count}";

    /// <summary>The 1-based position of <paramref name="price"/> among <paramref name="prices"/>, cheapest first; equal prices share a place.</summary>
    public static int PositionOf(decimal price, IEnumerable<decimal> prices) => prices.Count(p => p < price) + 1;

    public static string Ordinal(int n)
    {
        var text = n.ToString(CultureInfo.InvariantCulture);
        if (n % 100 is 11 or 12 or 13)
        {
            return text + "th";
        }

        return (n % 10) switch
        {
            1 => text + "st",
            2 => text + "nd",
            3 => text + "rd",
            _ => text + "th",
        };
    }

    /// <summary>"1 top sight", "12 top sights"; 500 or more reads "500+" (research R-1).</summary>
    public static string Count(int n, string one, string many, int cap = int.MaxValue) =>
        n >= cap ? $"{cap.ToString(CultureInfo.InvariantCulture)}+ {many}" : $"{n.ToString(CultureInfo.InvariantCulture)} {(n == 1 ? one : many)}";
}
