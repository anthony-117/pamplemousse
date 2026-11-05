namespace Pamplemousse.Domain.Ranking;

/// <summary>How a raw value becomes a measure from 0 to 1.</summary>
public enum Scale
{
    /// <summary>Already from 0 to 1 (e.g. a review score / 10). Doesn't depend on the other offers.</summary>
    Fixed,

    /// <summary>
    /// By position (price): the lowest in the list is 1, the highest 0, evenly spaced by how many offers are lower, so one
    /// very dear offer doesn't squeeze the others together (research R-3). Equal values share a position.
    /// </summary>
    LowerIsBetter,

    /// <summary>The value divided by the list's highest (nearby counts): none is 0, the most is 1.</summary>
    ShareOfMost,

    /// <summary>The list's lowest divided by the value (journey time): the shortest is 1.</summary>
    ShareOfLeast,
}

/// <summary>One raw value of an offer. A null value is unknown.</summary>
public sealed record Part(double? Value, Scale Scale);

/// <summary>
/// What an offer brings for one criterion: its parts (averaged when there are several), what was measured, and the
/// phrases a reason may use. <see cref="Best"/> replaces <see cref="High"/> when the measure is exactly 1.
/// </summary>
public sealed record CriterionInput(IReadOnlyList<Part> Parts, string Detail, string High, string Middle, string? Best = null);

/// <summary>An offer as the engine sees it. Only the criteria that apply to its kind are present.</summary>
public sealed record Candidate(string Id, int SourceRank, decimal Price, IReadOnlyDictionary<Criterion, CriterionInput> Criteria);

/// <summary>One preference in a match: how much it matters, how the offer measures on it (0 to 1), and what was measured.</summary>
public sealed record MatchPart(Criterion Criterion, int Weight, double Value, bool Known, string Detail)
{
    public int Percent => ToPercent(Value);

    internal static int ToPercent(double value) => (int)Math.Round(value * 100, MidpointRounding.AwayFromZero);
}

/// <summary>How well an offer matches a traveller's preferences (spec 007, FR-004 and FR-005).</summary>
public sealed record Match(double Total, IReadOnlyList<MatchPart> Parts, string Reason, bool OverBudget)
{
    public int Percent => MatchPart.ToPercent(Total);
}

public sealed record Ranked<T>(T Offer, Match Match);

/// <summary>
/// Ranks offers by a traveller's preferences (ADR 0002: deterministic C#, never the LLM). The same offers and preferences always
/// give the same order, matches and reasons (FR-009).
/// </summary>
public static class ScoringEngine
{
    /// <summary>A measure that can't be told apart from the others, or isn't known, counts as average.</summary>
    public const double Average = 0.5;

    private const double HighFrom = 0.8;
    private const double UsableFrom = 0.5;

    /// <summary>True when every criterion this kind uses has weight 0, so there is nothing to rank on (FR-010).</summary>
    public static bool NothingApplies(IEnumerable<Criterion> used, Weights weights)
    {
        ArgumentNullException.ThrowIfNull(used);
        ArgumentNullException.ThrowIfNull(weights);
        return used.All(c => weights[c] == 0);
    }

    /// <summary>
    /// Every offer with its match, best first; offers over the trip budget come after all others (FR-006).
    /// <paramref name="currency"/> is the currency of the prices and the trip budget.
    /// </summary>
    public static IReadOnlyList<Ranked<T>> Rank<T>(IReadOnlyList<T> offers, Func<T, Candidate> describe, Preferences preferences, string currency)
    {
        ArgumentNullException.ThrowIfNull(offers);
        ArgumentNullException.ThrowIfNull(describe);
        ArgumentNullException.ThrowIfNull(preferences);

        var candidates = offers.Select(describe).ToList();
        var lists = ListValues(candidates);
        return [.. offers
            .Select((offer, i) => (Offer: offer, Candidate: candidates[i], Match: Score(candidates[i], lists, preferences, currency)))
            .OrderBy(r => r.Match.OverBudget)
            .ThenByDescending(r => r.Match.Total)
            .ThenBy(r => r.Candidate.SourceRank)
            .ThenBy(r => r.Candidate.Id, StringComparer.Ordinal)
            .Select(r => new Ranked<T>(r.Offer, r.Match))];
    }

    /// <summary>The known values of each (criterion, part) across the list, for the list-relative scales.</summary>
    private static Dictionary<(Criterion, int), double[]> ListValues(List<Candidate> candidates) => candidates
        .SelectMany(c => c.Criteria.SelectMany(kv => kv.Value.Parts.Select((p, i) => (Key: (kv.Key, i), p.Value))))
        .Where(x => x.Value is not null)
        .GroupBy(x => x.Key, x => x.Value!.Value)
        .ToDictionary(g => g.Key, g => g.ToArray());

    private static Match Score(Candidate candidate, Dictionary<(Criterion, int), double[]> lists, Preferences preferences, string currency)
    {
        var parts = new List<MatchPart>();
        var phrases = new List<(double Contribution, string Phrase)>();
        foreach (var criterion in Enum.GetValues<Criterion>())
        {
            if (!candidate.Criteria.TryGetValue(criterion, out var input) || preferences.Weights[criterion] == 0)
            {
                continue;
            }

            var weight = preferences.Weights[criterion];
            var measures = input.Parts
                .Select((p, i) => Measure(p, lists.GetValueOrDefault((criterion, i), [])))
                .OfType<double>()
                .ToList();
            var known = measures.Count > 0;
            var value = known ? measures.Average() : Average;
            parts.Add(new MatchPart(criterion, weight, value, known, known ? input.Detail : "Unknown"));
            if (known && value >= UsableFrom)
            {
                var phrase = value < HighFrom ? input.Middle : value >= 1 && input.Best is not null ? input.Best : input.High;
                phrases.Add((weight * value, phrase));
            }
        }

        var totalWeight = parts.Sum(p => p.Weight);
        var total = totalWeight == 0 ? 0 : parts.Sum(p => p.Weight * p.Value) / totalWeight;
        var overBudget = preferences.TripBudget is { } budget && candidate.Price > budget;
        var reason = overBudget ? $"Over your {PriceText.Format(preferences.TripBudget!.Value, currency)} budget" : Reason(phrases, total);
        return new Match(total, parts, reason, overBudget);
    }

    /// <summary>A part's measure from 0 to 1, or null when unknown. List-relative scales count as average when the list can't tell offers apart.</summary>
    private static double? Measure(Part part, double[] list)
    {
        if (part.Value is not { } x)
        {
            return null;
        }

        if (part.Scale == Scale.Fixed)
        {
            return Math.Clamp(x, 0, 1);
        }

        var min = list.Min();
        var max = list.Max();
        if (list.Length < 2 || max - min < 1e-9)
        {
            return Average;
        }

        return part.Scale switch
        {
            Scale.LowerIsBetter => 1 - ((double)list.Count(v => v < x) / (list.Length - 1)),
            Scale.ShareOfMost => max <= 0 ? 0 : Math.Clamp(x / max, 0, 1),
            Scale.ShareOfLeast => x <= 0 ? 1 : Math.Clamp(min / x, 0, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(part), part.Scale, "Unknown scale."),
        };
    }

    /// <summary>The two largest contributions with a measure of at least 50%, as one short sentence (research R-4).</summary>
    private static string Reason(List<(double Contribution, string Phrase)> phrases, double total)
    {
        var best = phrases
            .OrderByDescending(p => p.Contribution)
            .Select(p => p.Phrase)
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToList();
        if (best.Count == 0)
        {
            return total >= 0.7 ? "Good all-round match" : "Weaker on what matters most to you";
        }

        return best.Count == 1 ? best[0] : $"{best[0]}, {LowerFirst(best[1])}";
    }

    private static string LowerFirst(string phrase) =>
        phrase.Length > 1 && char.IsUpper(phrase[0]) && !char.IsUpper(phrase[1]) ? char.ToLowerInvariant(phrase[0]) + phrase[1..] : phrase;
}
