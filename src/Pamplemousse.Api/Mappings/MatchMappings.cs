using Pamplemousse.Api.ViewModels;
using Pamplemousse.Application.Ranking;
using Pamplemousse.Domain.Ranking;

namespace Pamplemousse.Api.Mappings;

internal static class MatchMappings
{
    public static WeightsViewModel? WeightsOf(RankingResult? ranking) => ranking?.Weights is { } w
        ? new WeightsViewModel(w.Budget, w.Culture, w.Food, w.Nature, w.Nightlife, w.Comfort)
        : null;

    public static MatchViewModel? MatchOf(RankingResult? ranking, string id) =>
        ranking is not null && ranking.Matches.TryGetValue(id, out var match) ? match.ToViewModel() : null;

    private static MatchViewModel ToViewModel(this Match m) => new(
        m.Percent,
        m.Reason,
        m.OverBudget,
        [.. m.Parts.Select(p => new MatchPartViewModel(p.Criterion, p.Weight, p.Percent, p.Known, p.Detail))]);
}
