using System.Threading.Channels;
using Pamplemousse.Domain.Assistant;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Itinerary;
using Pamplemousse.Domain.Ranking;
using Pamplemousse.Domain.Travellers;

namespace Pamplemousse.Application.Assistant.Reply;

/// <summary>The trip of a message, as the app sends it; every value optional.</summary>
public sealed record TripInput(
    string? DestinationId,
    DateOnly? From,
    DateOnly? To,
    int? Adults,
    string? OriginId,
    string? Passport,
    string? Currency,
    decimal? TripBudget);

/// <summary>The six weights the app sends, 0–100; a missing one is 50.</summary>
public sealed record WeightsInput(int? Budget, int? Culture, int? Food, int? Nature, int? Nightlife, int? Comfort);

public sealed record AboutInput(string? PlanId, int? Day, string? ItemId);

/// <summary>
/// One message (spec 010 FR-001): to a new chat when <see cref="ChatId"/> is null, else to that chat. Trip, preferences
/// and pace left out keep the chat's, then the profile's.
/// </summary>
public sealed record SendMessageCommand(
    TravellerId Owner,
    string? ChatId,
    string? Text,
    TripInput? Trip,
    WeightsInput? Preferences,
    string? Pace,
    AboutInput? About);

/// <summary>What the stream says, in order: <see cref="ReplyStarted"/>, <see cref="ReplyStep"/>s, then <see cref="ReplyFinal"/> or <see cref="ReplyFailed"/>.</summary>
public abstract record ReplyEvent;

/// <summary><c>event: chat</c>: the chat's id and title, and the traveller's message id as stored.</summary>
public sealed record ReplyStarted(string ChatId, string Title, string MessageId) : ReplyEvent;

/// <summary><c>event: step</c>: a tool finished.</summary>
public sealed record ReplyStep(ToolStep Step) : ReplyEvent;

/// <summary><c>event: final</c>: the whole answer, as saved.</summary>
public sealed record ReplyFinal(ChatEntry Message, string Title) : ReplyEvent;

/// <summary><c>event: error</c>: the answer couldn't be saved.</summary>
public sealed record ReplyFailed(string Warning) : ReplyEvent;

/// <summary>A refused send has no stream; a started one has its events.</summary>
public sealed record ReplyAnswer(string ChatId, ChannelReader<ReplyEvent> Events);

/// <summary>Another send in this chat is still answering (spec Edge Cases). The API answers 409 <c>chat-busy</c>.</summary>
public sealed class ChatBusyException() : DomainException("This chat is still answering another message. Try again in a moment.");

/// <summary>The chat holds 200 messages (FR-022a). The API answers 409 <c>chat-full</c>.</summary>
public sealed class ChatFullException() : DomainException(ChatRules.FullMessage);

/// <summary>Everything one answer needs, handed from the request to the background run.</summary>
internal sealed record ReplyJob(
    Chat Chat,
    TripContext Trip,
    Weights Weights,
    Pace Pace,
    decimal? TripBudget,
    MessageAbout? About,
    int NextSequence);

/// <summary>
/// One answer's state (research R-2): each tool reports its step here, which goes to the stream at once, and its cards,
/// warnings and plan changes are kept for the final event.
/// </summary>
internal sealed class ReplyRun(ReplyJob job, ChannelWriter<ReplyEvent> events, TimeProvider clock, IReadOnlyList<string> earlierCards)
{
    /// <summary>The order of the final event's card groups.</summary>
    private static readonly string[] KindOrder = ["plan", "flight", "hotel", "car", "place", "event", "facts"];

    private readonly Lock _lock = new();
    private readonly List<ToolStep> _steps = [];
    private readonly List<string> _cards = [];
    private readonly List<string> _warnings = [];
    private readonly HashSet<string> _tools = new(StringComparer.Ordinal);

    public ReplyJob Job { get; } = job;

    public TravellerId Owner => Job.Chat.Owner;

    public DateTimeOffset Now => clock.GetUtcNow();

    public DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    public string? PlanChanged { get; private set; }

    public IReadOnlyList<ToolStep> Steps
    {
        get
        {
            lock (_lock)
            {
                return [.. _steps];
            }
        }
    }

    public IReadOnlyList<string> Warnings
    {
        get
        {
            lock (_lock)
            {
                return [.. _warnings];
            }
        }
    }

    public IReadOnlySet<string> ToolsRun
    {
        get
        {
            lock (_lock)
            {
                return new HashSet<string>(_tools, StringComparer.Ordinal);
            }
        }
    }

    /// <summary>The reply's cards, grouped by kind (plan, flight, hotel, car, place, event, facts), at most 6 of a kind, no repeats.</summary>
    public IReadOnlyList<string> Cards
    {
        get
        {
            lock (_lock)
            {
                return [.. _cards
                    .Select(c => (Json: c, Card: CardInfo.Read(c)))
                    .DistinctBy(c => (c.Card.Kind, c.Card.Id))
                    .GroupBy(c => c.Card.Kind, StringComparer.Ordinal)
                    .OrderBy(g => Array.IndexOf(KindOrder, g.Key) is var i && i >= 0 ? i : KindOrder.Length)
                    .SelectMany(g => g.Take(AssistantTools.MaxItems).Select(c => c.Json))];
            }
        }
    }

    /// <summary>A card seen in this answer or earlier in the chat, for <c>save_item</c>.</summary>
    public CardInfo? FindCard(string kind, string id)
    {
        lock (_lock)
        {
            return _cards.Concat(earlierCards).Select(CardInfo.Read)
                .FirstOrDefault(c => string.Equals(c.Kind, kind, StringComparison.Ordinal) && string.Equals(c.Id, id, StringComparison.Ordinal));
        }
    }

    public void Changed(string planId) => PlanChanged = planId;

    public void Warn(IEnumerable<string> warnings)
    {
        lock (_lock)
        {
            foreach (var w in warnings.Where(w => !_warnings.Contains(w, StringComparer.Ordinal)))
            {
                _warnings.Add(w);
            }
        }
    }

    /// <summary>Records a finished tool and sends its step at once (FR-002).</summary>
    public void Step(string tool, string summary, bool ok, int? total = null, IReadOnlyList<string>? cards = null)
    {
        var step = new ToolStep(tool, summary, ok, total, cards ?? []);
        lock (_lock)
        {
            _steps.Add(step);
            _cards.AddRange(step.Cards);
            _tools.Add(tool);
        }

        // Unbounded: never blocks, and a gone reader doesn't stop the answer.
        events.TryWrite(new ReplyStep(step));
    }
}
