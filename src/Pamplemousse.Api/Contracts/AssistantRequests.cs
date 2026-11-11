namespace Pamplemousse.Api.Contracts;

/// <summary>Body of POST /api/v1/assistant/messages: the recent conversation (at most 20) and the optional trip.</summary>
public sealed record AskAssistantRequest(IReadOnlyList<AssistantMessageRequest>? Messages, AssistantTripRequest? Context);

/// <summary><paramref name="Role"/> is "traveller" or "assistant".</summary>
public sealed record AssistantMessageRequest(string? Role, string? Text);

public sealed record AssistantTripRequest(
    string? DestinationId, DateOnly? From, DateOnly? To, int? Adults, string? Passport, string? Currency, string? OriginId);

/// <summary>A chat's new title, 1–40 characters.</summary>
public sealed record RenameChatRequest(string? Title);

/// <summary>
/// Body of the two chat sends (contracts/chat-api.md): the message, and the trip, preferences, pace and <see cref="About"/>
/// the composer sends with it.
/// </summary>
public sealed record SendMessageRequest(
    string? Text,
    ChatTripRequest? Trip,
    PreferenceWeightsRequest? Preferences,
    string? Pace,
    MessageAboutRequest? About);

public sealed record ChatTripRequest(
    string? DestinationId,
    DateOnly? From,
    DateOnly? To,
    int? Adults,
    string? OriginId,
    string? Passport,
    string? Currency,
    decimal? TripBudget);

public sealed record MessageAboutRequest(string? PlanId, int? Day, string? ItemId);
