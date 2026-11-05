namespace Pamplemousse.Api.Contracts;

/// <summary>Body of POST /api/v1/assistant/messages: the recent conversation (at most 20) and the optional trip.</summary>
public sealed record AskAssistantRequest(IReadOnlyList<AssistantMessageRequest>? Messages, AssistantTripRequest? Context);

/// <summary><paramref name="Role"/> is "traveller" or "assistant".</summary>
public sealed record AssistantMessageRequest(string? Role, string? Text);

public sealed record AssistantTripRequest(
    string? DestinationId, DateOnly? From, DateOnly? To, int? Adults, string? Passport, string? Currency, string? OriginId);
