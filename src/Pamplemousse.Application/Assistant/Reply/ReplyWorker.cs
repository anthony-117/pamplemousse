using System.Threading.Channels;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Assistant.Queries.AskAssistant;
using Pamplemousse.Domain.Assistant;

namespace Pamplemousse.Application.Assistant.Reply;

/// <summary>
/// One answer, in the background (research R-2, R-3): the model with the last 20 messages and the 21 tools; each tool's
/// step goes out as it finishes; then the answer is saved, the lease released and the final event sent. A missing key, a
/// busy model or 50 s end the answer with a warning, never an error (FR-007, SC-007). Chat text is never logged (FR-027).
/// </summary>
internal sealed partial class ReplyWorker(
    IChatClient chat,
    ReplyTools tools,
    IChatStore store,
    IReferenceData data,
    TimeProvider clock,
    ILogger<ReplyWorker> logger)
{
    /// <summary>Low, so answers stick to the tool results.</summary>
    private const float Temperature = 0.3f;

    public async Task RunAsync(ReplyJob job, ChannelWriter<ReplyEvent> events)
    {
        var owner = job.Chat.Owner;
        var history = await store.LastMessagesAsync(owner, job.Chat.Id, ChatRules.History, CancellationToken.None);
        var earlier = history.SelectMany(m => m.Cards.Concat(m.Steps.SelectMany(s => s.Cards))).ToList();
        var run = new ReplyRun(job, events, clock, earlier);

        var trip = job.Trip;
        var destination = trip.DestinationId is null ? null : data.FindDestination(trip.DestinationId);
        var origin = trip.OriginId is null ? null : data.FindDestination(trip.OriginId);
        List<ChatMessage> messages =
        [
            new(ChatRole.System, ReplyInstructions.For(trip, destination, origin, job.Weights, job.Pace, job.About, run.Today)),
            .. history
                .Where(m => m.Text.Length > 0)
                .Select(m => new ChatMessage(m.Role == Speaker.Traveller ? ChatRole.User : ChatRole.Assistant, m.Text)),
        ];

        var (text, warning) = await AskAsync(messages, new ChatOptions { Tools = tools.For(run), ToolMode = ChatToolMode.Auto, Temperature = Temperature }, job.Chat.Id);
        var warnings = warning is null ? run.Warnings : [.. run.Warnings, warning];
        var message = new ChatEntry(
            ChatRules.MessageId(job.NextSequence),
            Speaker.Assistant,
            text,
            clock.GetUtcNow(),
            null,
            run.Steps,
            run.Cards,
            Suggestions.For(run.ToolsRun, text.Length > 0),
            warnings,
            run.PlanChanged);

        var latest = await store.GetAsync(owner, job.Chat.Id, CancellationToken.None) ?? job.Chat;
        var saved = latest.With(message) with { BusyUntil = null };
        await store.SaveAsync(saved, message, CancellationToken.None);
        LogAnswered(logger, job.Chat.Id, run.Steps.Count, message.Cards.Count);
        events.TryWrite(new ReplyFinal(message, saved.Title));
    }

    private async Task<(string Text, string? Warning)> AskAsync(List<ChatMessage> messages, ChatOptions options, string chatId)
    {
        using var limit = new CancellationTokenSource(AskAssistantHandler.AnswerTimeLimit);
        try
        {
            var response = await chat.GetResponseAsync(messages, options, limit.Token);

            // With tool calls the response holds several assistant messages; the answer is the last one with text.
            var text = response.Messages.LastOrDefault(m => m.Role == ChatRole.Assistant && !string.IsNullOrWhiteSpace(m.Text))?.Text.Trim() ?? "";
            return text.Length == 0 ? ("", AskAssistantHandler.NoAnswerWarning) : (text, null);
        }
        catch (ProviderNotConfiguredException ex)
        {
            return ("", ex.Message);
        }
        catch (AssistantUnavailableException ex)
        {
            LogUnavailable(logger, chatId, ex);
            return ("", AskAssistantHandler.BusyWarning);
        }
        catch (OperationCanceledException ex)
        {
            LogUnavailable(logger, chatId, ex);
            return ("", AskAssistantHandler.BusyWarning);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Chat {ChatId} answered with {Steps} steps and {Cards} cards")]
    private static partial void LogAnswered(ILogger logger, string chatId, int steps, int cards);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The chat model could not answer chat {ChatId}")]
    private static partial void LogUnavailable(ILogger logger, string chatId, Exception exception);
}
