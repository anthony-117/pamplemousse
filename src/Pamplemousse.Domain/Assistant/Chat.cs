using System.Globalization;
using System.Text;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Itinerary;
using Pamplemousse.Domain.Ranking;
using Pamplemousse.Domain.Travellers;

namespace Pamplemousse.Domain.Assistant;

/// <summary>The limits of spec 010 (FR-022a, FR-025, FR-026, research R-3). They can change without changing the API.</summary>
public static class ChatRules
{
    public const int MaxMessages = 200;
    public const int MaxChats = 500;
    public const int KeepDays = 90;

    /// <summary>How many of the last messages the model sees.</summary>
    public const int History = 20;

    public const int MaxTitleLength = 40;
    public const int MaxTerms = 2000;

    /// <summary>How long a send holds the chat (the answer's 50 s and saving it).</summary>
    public static readonly TimeSpan Lease = TimeSpan.FromSeconds(70);

    public const string FullMessage = "This chat is full. Start a new chat to carry on.";

    /// <summary>"0001": the message's id, its order in the chat.</summary>
    public static string MessageId(int sequence) => sequence.ToString("D4", CultureInfo.InvariantCulture);
}

/// <summary>The trip a chat is about, as the traveller set it: every value optional (spec 010 FR-023).</summary>
public sealed record TripContext(
    string? DestinationId,
    DateOnly? From,
    DateOnly? To,
    int? Adults,
    string? OriginId,
    string? Passport,
    string? Currency,
    decimal? TripBudget)
{
    public bool IsEmpty => DestinationId is null && From is null && To is null && Adults is null && OriginId is null;
}

/// <summary>What a message is about: a plan, a day of it, or an item of that day.</summary>
public sealed record MessageAbout(string PlanId, int? Day, string? ItemId)
{
    public static MessageAbout? Create(string? planId, int? day, string? itemId)
    {
        if (string.IsNullOrWhiteSpace(planId))
        {
            return day is null && itemId is null ? null : throw new DomainException("Name the plan.", "about.planId");
        }

        if (day is < 1)
        {
            throw new DomainException("Choose a day of the plan.", "about.day");
        }

        if (!string.IsNullOrWhiteSpace(itemId) && day is null)
        {
            throw new DomainException("Name the item's day.", "about.day");
        }

        return new MessageAbout(planId.Trim(), day, string.IsNullOrWhiteSpace(itemId) ? null : itemId.Trim());
    }
}

/// <summary>One tool the assistant ran (FR-002): the summary is the server's. <see cref="Cards"/> are card JSON.</summary>
public sealed record ToolStep(string Tool, string Summary, bool Ok, int? Total, IReadOnlyList<string> Cards);

/// <summary>A message of a chat (FR-023). Cards are kept as the JSON the app received (research R-4).</summary>
public sealed record ChatEntry(
    string Id,
    Speaker Role,
    string Text,
    DateTimeOffset At,
    MessageAbout? About,
    IReadOnlyList<ToolStep> Steps,
    IReadOnlyList<string> Cards,
    IReadOnlyList<string> Suggestions,
    IReadOnlyList<string> Warnings,
    string? PlanChanged)
{
    public const int MaxTextLength = 1000;

    /// <summary>The traveller's message: 1–1,000 characters after trimming, naming <c>text</c>.</summary>
    public static ChatEntry FromTraveller(int sequence, string? text, MessageAbout? about, DateTimeOffset at)
    {
        var trimmed = text?.Trim() ?? "";
        if (trimmed.Length is 0 or > MaxTextLength)
        {
            throw new DomainException($"Write 1 to {MaxTextLength} characters.", "text");
        }

        return new ChatEntry(ChatRules.MessageId(sequence), Speaker.Traveller, trimmed, at, about, [], [], [], [], null);
    }
}

/// <summary>A chat of one account (FR-022 to FR-026). Its messages are stored apart (research R-6).</summary>
public sealed record Chat(
    string Id,
    TravellerId Owner,
    string Title,
    TripContext? Trip,
    Weights? Preferences,
    Pace? Pace,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastMessageAt,
    int MessageCount,
    DateTimeOffset? BusyUntil,
    IReadOnlyList<string> Terms)
{
    public DateTimeOffset ExpiresAt => LastMessageAt.AddDays(ChatRules.KeepDays);

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    public bool IsBusy(DateTimeOffset now) => BusyUntil is { } until && until > now;

    public bool IsFull => MessageCount >= ChatRules.MaxMessages;

    /// <summary>A new chat, titled from its first message (R-9).</summary>
    public static Chat Start(string id, TravellerId owner, string firstMessage, DateTimeOffset now) =>
        new(id, owner, ChatTitle.From(firstMessage), null, null, null, now, now, 0, null, ChatTerms.Of([firstMessage]));

    public Chat Rename(string? title)
    {
        var trimmed = title?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > ChatRules.MaxTitleLength
            ? throw new DomainException($"Use 1 to {ChatRules.MaxTitleLength} characters.", "title")
            : this with { Title = trimmed, Terms = ChatTerms.Merge(Terms, [trimmed]) };
    }

    /// <summary>The chat after <paramref name="message"/>: one more message, its words searchable.</summary>
    public Chat With(ChatEntry message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return this with
        {
            MessageCount = MessageCount + 1,
            LastMessageAt = message.At,
            Terms = ChatTerms.Merge(Terms, [message.Text]),
        };
    }
}

/// <summary>A chat's title: the first message's first 40 characters, cut at a word (R-9).</summary>
public static class ChatTitle
{
    public static string From(string? text)
    {
        var words = (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return "New chat";
        }

        var title = new StringBuilder();
        foreach (var word in words)
        {
            var next = title.Length == 0 ? word : " " + word;
            if (title.Length + next.Length > ChatRules.MaxTitleLength)
            {
                break;
            }

            title.Append(next);
        }

        var result = title.Length == 0 ? words[0][..ChatRules.MaxTitleLength] : title.ToString();
        return result.TrimEnd(',', '.', ';', ':', '!', '?', '-', '–');
    }
}

/// <summary>The searchable words of a chat (R-7): lower case, no accents, letters and digits, 2–30 long.</summary>
public static class ChatTerms
{
    public static IReadOnlyList<string> Of(IEnumerable<string> texts) => Merge([], texts);

    public static IReadOnlyList<string> Merge(IReadOnlyList<string> terms, IEnumerable<string> texts)
    {
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(texts);
        var all = new List<string>(terms);
        var seen = new HashSet<string>(terms, StringComparer.Ordinal);
        foreach (var word in texts.SelectMany(Words))
        {
            if (all.Count >= ChatRules.MaxTerms)
            {
                break;
            }

            if (seen.Add(word))
            {
                all.Add(word);
            }
        }

        return all;
    }

    /// <summary>The words of <paramref name="text"/>, as search compares them.</summary>
    public static IEnumerable<string> Words(string? text)
    {
        var plain = new StringBuilder();
        foreach (var c in (text ?? "").Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            plain.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }

        return plain.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length is >= 2 and <= 30)
            .Distinct(StringComparer.Ordinal);
    }
}
