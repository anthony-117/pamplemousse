using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Assistant;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Domain.Exceptions;

namespace Pamplemousse.Application.Assistant.Queries.AskAssistant;

/// <summary>One message of the chat. <paramref name="Role"/> is "traveller" or "assistant".</summary>
public sealed record AssistantMessage(string? Role, string? Text);

/// <summary>The trip the chat is about; every value is optional.</summary>
public sealed record AssistantTripInput(
    string? DestinationId, DateOnly? From, DateOnly? To, int? Adults, string? Passport, string? Currency, string? OriginId);

public sealed record AskAssistantQuery(IReadOnlyList<AssistantMessage>? Messages, AssistantTripInput? Trip);

/// <summary><see cref="Text"/> is empty when the assistant could not answer; <see cref="Warnings"/> then says why.</summary>
public sealed record AskAssistantResponse(string Text, IReadOnlyList<AssistantCard> Cards, IReadOnlyList<string> Warnings);

/// <summary>
/// Answers the traveller's last message with the chat model and the read-only tools (spec 003, ADR 0018).
/// Nothing about the conversation is stored or logged (FR-010).
/// </summary>
public sealed partial class AskAssistantHandler(
    IChatClient chat,
    AssistantTools tools,
    IReferenceData data,
    TimeProvider clock,
    ILogger<AskAssistantHandler> logger) : IQueryHandler<AskAssistantQuery, AskAssistantResponse>
{
    public const string BusyWarning = "The assistant is busy right now. Try again in a minute.";
    public const string NoAnswerWarning = "The assistant had no answer. Try asking another way.";

    /// <summary>Longest a whole answer may take, tool calls included; past it the answer is <see cref="BusyWarning"/>. The app waits a little longer.</summary>
    public static readonly TimeSpan AnswerTimeLimit = TimeSpan.FromSeconds(50);

    /// <summary>Low, so answers stick to the tool results.</summary>
    private const float Temperature = 0.3f;

    public async Task<AskAssistantResponse> HandleAsync(AskAssistantQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var conversation = Conversation.Create(query.Messages?.Select(m => (m.Role, m.Text)).ToList());
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var t = query.Trip;
        var trip = ChatTrip.Create(t?.DestinationId, t?.From, t?.To, t?.Adults, t?.Passport, t?.Currency, t?.OriginId, today);
        var destination = Find(trip.DestinationId, "context.destinationId");
        var origin = Find(trip.OriginId, "context.originId");
        var country = destination is null ? null : data.FindCountry(destination.CountryCode)?.Name;

        List<ChatMessage> messages =
        [
            new(ChatRole.System, AssistantInstructions.For(trip, destination, country, origin, today)),
            .. conversation.Turns.Select(turn => new ChatMessage(turn.Speaker == Speaker.Traveller ? ChatRole.User : ChatRole.Assistant, turn.Text)),
        ];

        var session = new AssistantSession();
        var options = new ChatOptions { Tools = tools.For(trip, session), ToolMode = ChatToolMode.Auto, Temperature = Temperature };
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(AnswerTimeLimit);
        try
        {
            var response = await chat.GetResponseAsync(messages, options, limit.Token);

            // With tool calls the response holds several assistant messages; the answer is the last one with text.
            var text = response.Messages.LastOrDefault(m => m.Role == ChatRole.Assistant && !string.IsNullOrWhiteSpace(m.Text))?.Text.Trim() ?? "";
            return text.Length == 0
                ? new AskAssistantResponse("", [], [NoAnswerWarning])
                : new AskAssistantResponse(text, session.Cards, session.Warnings);
        }
        catch (ProviderNotConfiguredException ex)
        {
            return new AskAssistantResponse("", [], [ex.Message]);
        }
        catch (AssistantUnavailableException ex)
        {
            LogUnavailable(logger, ex);
            return new AskAssistantResponse("", [], [BusyWarning]);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            LogUnavailable(logger, ex);
            return new AskAssistantResponse("", [], [BusyWarning]);
        }
    }

    private Destination? Find(string? id, string field) => id is null
        ? null
        : data.FindDestination(id) ?? throw new NotFoundException($"Destination '{id}' ({field}) was not found.");

    [LoggerMessage(Level = LogLevel.Warning, Message = "The chat model could not answer")]
    private static partial void LogUnavailable(ILogger logger, Exception exception);
}
