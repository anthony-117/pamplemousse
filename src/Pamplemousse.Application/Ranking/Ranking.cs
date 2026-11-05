using Pamplemousse.Domain.Ranking;

namespace Pamplemousse.Application.Ranking;

/// <summary>The preferences and trip budget sent with a list request, unchecked (spec 007, FR-001 and FR-002).</summary>
public sealed record RankingInput(int? Budget, int? Culture, int? Food, int? Nature, int? Nightlife, int? Comfort, decimal? TripBudget)
{
    /// <summary>Null when nothing was sent. Out-of-range values throw, naming the field.</summary>
    public Preferences? ToPreferences() => Preferences.Create(Budget, Culture, Food, Nature, Nightlife, Comfort, TripBudget);
}

/// <summary>
/// How a list was ranked: the preferences used, each offer's match by offer id, and a note when there was nothing to rank on
/// (FR-010; <see cref="Matches"/> is then empty).
/// </summary>
public sealed record RankingResult(Weights Weights, decimal? TripBudget, IReadOnlyDictionary<string, Match> Matches, string? Note)
{
    /// <summary>
    /// The preferences to rank with: those sent, or neutral ones when only sort=match was asked. Null when the list should not be
    /// ranked at all.
    /// </summary>
    public static Preferences? PreferencesFor(RankingInput? input, bool sortByMatch)
    {
        var sent = input?.ToPreferences();
        return sent ?? (sortByMatch ? new Preferences(Weights.Neutral, null) : null);
    }

    public static RankingResult Unranked(Preferences preferences, string kind) => new(
        preferences.Weights,
        preferences.TripBudget,
        new Dictionary<string, Match>(StringComparer.Ordinal),
        $"None of your preferences apply to {kind}, so they are in the usual order.");

    public static RankingResult Of<T>(Preferences preferences, IEnumerable<Ranked<T>> ranked, Func<T, string> id) => new(
        preferences.Weights,
        preferences.TripBudget,
        ranked.ToDictionary(r => id(r.Offer), r => r.Match, StringComparer.Ordinal),
        null);
}
