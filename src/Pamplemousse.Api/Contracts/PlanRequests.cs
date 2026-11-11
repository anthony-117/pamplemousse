namespace Pamplemousse.Api.Contracts;

/// <summary>
/// What a plan is for (contracts/itinerary-api.md). <see cref="OriginId"/>, <see cref="Pace"/> and <see cref="TripBudget"/>
/// are optional; a preference left out is 50.
/// </summary>
public sealed record BuildPlanRequest(
    string? DestinationId,
    DateOnly? From,
    DateOnly? To,
    int? Adults,
    string? Currency,
    string? OriginId,
    PreferenceWeightsRequest? Preferences,
    string? Pace,
    decimal? TripBudget);

/// <summary>A new name, a new status ("draft" or "planned"), or both.</summary>
public sealed record ChangePlanRequest(string? Name, string? Status);

/// <summary>An offer from the plan's kept list of that kind.</summary>
public sealed record SwapOfferRequest(string? OfferId);

/// <summary>
/// A day's alternative to add, or to put in place of <see cref="Replaces"/> (spec 010, contracts/plan-edits-api.md).
/// <see cref="Kind"/> is place or event.
/// </summary>
public sealed record ChangeDayItemRequest(string? Kind, string? Id, string? Replaces);

/// <summary>The values a rebuild changes; a field left out keeps the plan's (spec 010 FR-012a).</summary>
public sealed record RebuildPlanRequest(
    DateOnly? From,
    DateOnly? To,
    int? Adults,
    string? OriginId,
    PreferenceWeightsRequest? Preferences,
    string? Pace,
    decimal? TripBudget);
