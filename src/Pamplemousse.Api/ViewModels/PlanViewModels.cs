using Pamplemousse.Domain.Itinerary;
using Pamplemousse.Domain.Ranking;

namespace Pamplemousse.Api.ViewModels;

/// <summary>A saved plan (spec 008, contracts/itinerary-api.md). Times are local to the destination, "HH:mm".</summary>
public sealed record PlanViewModel(
    string Id,
    string Name,
    PlanStatus Status,
    PlanDestinationViewModel Destination,
    DateOnly From,
    DateOnly To,
    int Adults,
    string Currency,
    string? OriginId,
    WeightsViewModel Preferences,
    string Pace,
    decimal? TripBudget,
    PlanMatchViewModel? Match,
    ChosenOfferViewModel<HotelViewModel>? Hotel,
    ChosenOfferViewModel<FlightViewModel>? Flight,
    ChosenOfferViewModel<CarViewModel>? Car,
    PlanTotalViewModel Total,
    IReadOnlyList<PlanDayViewModel> Days,
    IReadOnlyList<string> Notes,
    IReadOnlyList<string> Warnings,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<PlanEditViewModel> Edits);

/// <summary>A day edit the traveller made (spec 010), kept through every rebuild.</summary>
public sealed record PlanEditViewModel(int Day, EditAction Action, string? ItemId, EditItemViewModel? NewItem);

public sealed record EditItemViewModel(RefKind Kind, string Id, string Name);

public sealed record PlanDestinationViewModel(string Id, string Name, string CountryCode);

/// <summary>The offer the plan uses, as its list shows it with its match, and when its price was checked (FR-013).</summary>
public sealed record ChosenOfferViewModel<T>(T Offer, DateTimeOffset? CheckedAt);

/// <summary>The plan's match (FR-009): the parts it is the weighted average of, and how well the days fit each interest.</summary>
public sealed record PlanMatchViewModel(int Percent, string Reason, IReadOnlyList<PlanMatchPartViewModel> Parts, IReadOnlyList<DaysFitViewModel> Days);

public sealed record PlanMatchPartViewModel(MatchPartKind Part, int Weight, int Percent);

public sealed record DaysFitViewModel(Criterion Criterion, int Weight, int Percent);

/// <summary>What the plan costs in its currency (FR-010). A part is null when the plan has none.</summary>
public sealed record PlanTotalViewModel(
    decimal Amount,
    string Currency,
    decimal? Flight,
    decimal? Hotel,
    decimal? Car,
    decimal Events,
    int Unpriced,
    int OtherCurrency,
    decimal? OverBudgetBy);

public sealed record PlanDayViewModel(int Number, DateOnly Date, string Title, PlanWeatherViewModel? Weather, double WalkKm, IReadOnlyList<PlanItemViewModel> Items);

public sealed record PlanWeatherViewModel(string Summary, double MaxC, double MinC, int? RainChance);

public sealed record PlanItemViewModel(ItemKind Kind, string Start, int Minutes, string Name, double WalkKm, MoneyViewModel? Price, ItemRefViewModel? Ref);

/// <summary>What an item is a copy of: a place, an event, the flight or the hotel.</summary>
public sealed record ItemRefViewModel(RefKind Kind, string Id, double? Lat, double? Lng, string? ImageUrl, string? Url);

/// <summary>The "My plans" list, newest change first, with the most a traveller can keep.</summary>
public sealed record PlanListViewModel(IReadOnlyList<PlanSummaryViewModel> Items, int Count, int Max);

public sealed record PlanSummaryViewModel(
    string Id,
    string Name,
    PlanStatus Status,
    string DestinationName,
    DateOnly From,
    DateOnly To,
    decimal Total,
    string Currency,
    int? MatchPercent,
    string? ImageUrl,
    DateTimeOffset UpdatedAt,
    string? DestinationId,
    int? Adults,
    decimal? TripBudget,
    string? Pace,
    double? Lat,
    double? Lng);

/// <summary>A kept ranked list of a plan (research R-7), each item as its list shows it, with its match.</summary>
public sealed record PlanAlternativesViewModel<T>(OfferKind Kind, string? ChosenId, DateTimeOffset? CheckedAt, IReadOnlyList<T> Items);

/// <summary>
/// The alternatives for a day (spec 010 FR-017): place or event cards, each with how far it is from the day's other
/// items. The cards are the envelope of contracts/chat-api.md, so they are written as JSON (<c>CardJson</c>).
/// </summary>
public sealed record DayAlternativesViewModel(int Day, string? ItemId, AlternativeKind Kind, IReadOnlyList<System.Text.Json.Nodes.JsonObject> Items, string? Message);
