using Pamplemousse.Domain.Ranking;

namespace Pamplemousse.Api.ViewModels;

/// <summary>How well an offer fits the preferences (spec 007): a percentage, a short reason and the breakdown behind it.</summary>
public sealed record MatchViewModel(int Percent, string Reason, bool OverBudget, IReadOnlyList<MatchPartViewModel> Parts);

/// <summary>One preference's share of a match. <see cref="Known"/> is false when the data was missing and counted as average.</summary>
public sealed record MatchPartViewModel(Criterion Criterion, int Weight, int Percent, bool Known, string Detail);

/// <summary>The preference weights a list was ranked with, 0 to 100 each.</summary>
public sealed record WeightsViewModel(int Budget, int Culture, int Food, int Nature, int Nightlife, int Comfort);
