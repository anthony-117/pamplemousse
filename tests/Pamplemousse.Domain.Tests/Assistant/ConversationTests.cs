using Pamplemousse.Domain.Assistant;
using Pamplemousse.Domain.Exceptions;

namespace Pamplemousse.Domain.Tests.Assistant;

public class ConversationTests
{
    [Fact]
    public void Keeps_the_turns_in_order_with_trimmed_text_and_any_case_roles()
    {
        var conversation = Conversation.Create([("Traveller", " Visa? "), ("ASSISTANT", "Which passport?"), ("traveller", "Lebanese")]);

        Assert.Equal(
            [new ChatTurn(Speaker.Traveller, "Visa?"), new ChatTurn(Speaker.Assistant, "Which passport?"), new ChatTurn(Speaker.Traveller, "Lebanese")],
            conversation.Turns);
        Assert.Equal("Lebanese", conversation.Question);
    }

    [Fact]
    public void Accepts_exactly_the_maximum_number_of_messages()
    {
        // Alternates assistant/traveller, so the 20th message is the traveller's.
        var messages = Enumerable.Range(0, Conversation.MaxMessages).Select(i => ((string?)(i % 2 == 0 ? "assistant" : "traveller"), (string?)"hi")).ToList();

        Assert.Equal(Conversation.MaxMessages, Conversation.Create(messages).Turns.Count);
    }

    [Fact]
    public void Accepts_text_of_exactly_the_maximum_length()
    {
        var text = new string('a', Conversation.MaxTextLength);

        Assert.Equal(text, Conversation.Create([("traveller", text)]).Question);
    }

    public static TheoryData<string, (string?, string?)[]> BrokenConversations => new()
    {
        { "no messages", [] },
        { "empty text", [("traveller", "  ")] },
        { "missing text", [("traveller", null)] },
        { "too long", [("traveller", new string('a', Conversation.MaxTextLength + 1))] },
        { "unknown role", [("user", "Hi")] },
        { "missing role", [(null, "Hi")] },
        { "ends with the assistant", [("traveller", "Hi"), ("assistant", "Hello!")] },
    };

    [Theory]
    [MemberData(nameof(BrokenConversations))]
    public void Rejects_a_broken_conversation_naming_the_messages_field(string why, (string?, string?)[] messages)
    {
        var ex = Assert.Throws<DomainException>(() => Conversation.Create(messages));

        Assert.True(string.Equals(ex.Field, "messages", StringComparison.Ordinal), why);
    }

    [Fact]
    public void Rejects_null_and_more_than_the_maximum_number_of_messages()
    {
        var tooMany = Enumerable.Repeat<(string?, string?)>(("traveller", "hi"), Conversation.MaxMessages + 1).ToList();

        Assert.Throws<DomainException>(() => Conversation.Create(null));
        Assert.Throws<DomainException>(() => Conversation.Create(tooMany));
    }
}
