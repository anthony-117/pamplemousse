using Pamplemousse.Domain.Exceptions;

namespace Pamplemousse.Domain.Assistant;

/// <summary>Who wrote a message in the assistant chat.</summary>
public enum Speaker
{
    Traveller,
    Assistant,
}

/// <summary>One message of the chat, already checked by <see cref="Conversation.Create"/>.</summary>
public sealed record ChatTurn(Speaker Speaker, string Text);

/// <summary>
/// The recent chat the app sends with each question (spec 003, FR-001). The server keeps none of it.
/// Only <see cref="Create"/> makes one, so every instance follows the rules.
/// </summary>
public sealed record Conversation
{
    public const int MaxMessages = 20;
    public const int MaxTextLength = 1000;

    private Conversation(IReadOnlyList<ChatTurn> turns) => Turns = turns;

    public IReadOnlyList<ChatTurn> Turns { get; }

    /// <summary>The traveller's question being answered: always the last message.</summary>
    public string Question => Turns[^1].Text;

    /// <summary>1–20 messages, roles "traveller" or "assistant" in any case, texts of 1–1,000 characters, ending with the traveller.</summary>
    public static Conversation Create(IReadOnlyList<(string? Role, string? Text)>? messages)
    {
        if (messages is null || messages.Count == 0)
        {
            throw new DomainException("Send at least one message.", "messages");
        }

        if (messages.Count > MaxMessages)
        {
            throw new DomainException($"Send at most the last {MaxMessages} messages.", "messages");
        }

        var turns = messages.Select(m => new ChatTurn(ParseSpeaker(m.Role), ParseText(m.Text))).ToList();
        if (turns[^1].Speaker != Speaker.Traveller)
        {
            throw new DomainException("The last message must be the traveller's.", "messages");
        }

        return new Conversation(turns);
    }

    private static Speaker ParseSpeaker(string? role) => role?.Trim().ToUpperInvariant() switch
    {
        "TRAVELLER" => Speaker.Traveller,
        "ASSISTANT" => Speaker.Assistant,
        _ => throw new DomainException("A message's role must be 'traveller' or 'assistant'.", "messages"),
    };

    private static string ParseText(string? text)
    {
        var trimmed = text?.Trim() ?? "";
        if (trimmed.Length == 0 || trimmed.Length > MaxTextLength)
        {
            throw new DomainException($"Each message must have 1 to {MaxTextLength} characters.", "messages");
        }

        return trimmed;
    }
}
