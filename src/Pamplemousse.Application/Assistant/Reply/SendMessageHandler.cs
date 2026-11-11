using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Assistant;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Itinerary;
using Pamplemousse.Domain.Ranking;

namespace Pamplemousse.Application.Assistant.Reply;

/// <summary>
/// Sends one message (spec 010 FR-001): checks it, takes the chat's lease, saves the traveller's message and starts the
/// answer in the background (research R-3). Refusals throw before any event: a field (400), not found (404),
/// <see cref="ChatBusyException"/> or <see cref="ChatFullException"/> (409).
/// </summary>
public sealed partial class SendMessageHandler(
    IChatStore store,
    IReferenceData data,
    ITravelProfileStore profiles,
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<SendMessageHandler> logger) : ICommandHandler<SendMessageCommand, ReplyAnswer>
{
    public async Task<ReplyAnswer> HandleAsync(SendMessageCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var now = clock.GetUtcNow();
        ChatEntry.FromTraveller(1, command.Text, null, now); // checks the text first, naming the field
        var about = MessageAbout.Create(command.About?.PlanId, command.About?.Day, command.About?.ItemId);
        var trip = TripOf(command.Trip);
        var p = command.Preferences;
        var weights = p is null ? null : Weights.Create(p.Budget, p.Culture, p.Food, p.Nature, p.Nightlife, p.Comfort);
        Pace? pace = string.IsNullOrWhiteSpace(command.Pace) ? null : PaceRules.Parse(command.Pace);
        var until = now + ChatRules.Lease;

        var (chat, message) = await OpenAsync(command, about, trip, weights, pace, now, until, ct);

        var profile = await profiles.GetAsync(command.Owner, ct);
        var context = chat.Trip ?? new TripContext(null, null, null, null, null, null, null, null);
        var job = new ReplyJob(
            chat,
            context,
            chat.Preferences ?? profile?.Preferences ?? Weights.Neutral,
            chat.Pace ?? profile?.Pace ?? Pace.Balanced,
            context.TripBudget,
            about,
            chat.MessageCount + 1);

        var events = Channel.CreateUnbounded<ReplyEvent>(new UnboundedChannelOptions { SingleReader = true });
        events.Writer.TryWrite(new ReplyStarted(chat.Id, chat.Title, message.Id));
        _ = Task.Run(() => AnswerAsync(job, events.Writer), CancellationToken.None);
        return new ReplyAnswer(chat.Id, events.Reader);
    }

    /// <summary>Starts the chat, or holds an existing one, with the traveller's message saved in it.</summary>
    private async Task<(Chat Chat, ChatEntry Message)> OpenAsync(
        SendMessageCommand command, MessageAbout? about, TripContext? trip, Weights? weights, Pace? pace, DateTimeOffset now, DateTimeOffset until, CancellationToken ct)
    {
        Chat chat;
        ChatEntry message;
        if (command.ChatId is null)
        {
            // The 501st chat makes room by deleting the one with the oldest last message (FR-022a).
            if (await store.CountAsync(command.Owner, ct) >= ChatRules.MaxChats && await store.OldestAsync(command.Owner, ct) is { } oldest)
            {
                await store.DeleteAsync(command.Owner, oldest.Id, ct);
            }

            message = ChatEntry.FromTraveller(1, command.Text, about, now);
            chat = Chat.Start(Guid.NewGuid().ToString("N"), command.Owner, message.Text, now) with { Trip = trip, Preferences = weights, Pace = pace, BusyUntil = until };
            chat = chat.With(message);
            await store.CreateAsync(chat, message, ct);
        }
        else
        {
            var found = await store.GetAsync(command.Owner, command.ChatId.Trim(), ct);
            if (found is null || found.IsExpired(now))
            {
                throw new NotFoundException("The chat was not found.");
            }

            if (found.IsFull)
            {
                throw new ChatFullException();
            }

            if (!await store.TryTakeLeaseAsync(command.Owner, found.Id, now, until, ct))
            {
                throw new ChatBusyException();
            }

            message = ChatEntry.FromTraveller(found.MessageCount + 1, command.Text, about, now);
            chat = (found with
            {
                Trip = trip ?? found.Trip,
                Preferences = weights ?? found.Preferences,
                Pace = pace ?? found.Pace,
                BusyUntil = until,
            }).With(message);
            await store.SaveAsync(chat, message, ct);
        }

        return (chat, message);
    }

    /// <summary>Runs in its own scope, so it outlives the request (research R-3). It never throws.</summary>
    private async Task AnswerAsync(ReplyJob job, ChannelWriter<ReplyEvent> events)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ReplyWorker>().RunAsync(job, events);
        }
        catch (Exception ex)
        {
            LogAnswerFailed(logger, job.Chat.Id, ex);
            events.TryWrite(new ReplyFailed("The answer couldn't be saved. Try again."));
        }
        finally
        {
            events.TryComplete();
        }
    }

    /// <summary>The trip as sent, checked field by field (<c>trip.…</c>); null when nothing was set.</summary>
    private TripContext? TripOf(TripInput? t)
    {
        if (t is null)
        {
            return null;
        }

        string? Known(string? id, string field) => string.IsNullOrWhiteSpace(id)
            ? null
            : data.FindDestination(id.Trim()) is { } d ? d.Id : throw new DomainException("Choose a city from the list.", field);
        string? Code(string? code, int length, string field) => string.IsNullOrWhiteSpace(code)
            ? null
            : code.Trim().Length == length && code.Trim().All(char.IsAsciiLetter)
                ? code.Trim().ToUpperInvariant()
                : throw new DomainException($"Use a {length}-letter code.", field);

        if (t.Adults is < 1 or > ChatTrip.MaxAdults)
        {
            throw new DomainException($"Adults must be between 1 and {ChatTrip.MaxAdults}.", "trip.adults");
        }

        if (t.From is { } from && t.To is { } to && (to < from || to.DayNumber - from.DayNumber + 1 > TripRequest.MaxDays))
        {
            throw new DomainException($"A trip covers 1 to {TripRequest.MaxDays} days.", "trip.to");
        }

        if (t.TripBudget is <= 0)
        {
            throw new DomainException("The budget must be more than 0.", "trip.tripBudget");
        }

        var trip = new TripContext(
            Known(t.DestinationId, "trip.destinationId"),
            t.From,
            t.To,
            t.Adults,
            Known(t.OriginId, "trip.originId"),
            Code(t.Passport, 2, "trip.passport"),
            Code(t.Currency, 3, "trip.currency"),
            t.TripBudget);
        return trip.IsEmpty && trip.Passport is null && trip.Currency is null && trip.TripBudget is null ? null : trip;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The answer for chat {ChatId} failed")]
    private static partial void LogAnswerFailed(ILogger logger, string chatId, Exception exception);
}
