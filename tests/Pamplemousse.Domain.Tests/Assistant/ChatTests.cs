using Pamplemousse.Domain.Assistant;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Travellers;

namespace Pamplemousse.Domain.Tests.Assistant;

/// <summary>Spec 010 FR-022 to FR-026 and research R-7, R-9.</summary>
public class ChatTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);
    private static readonly TravellerId Fares = new("uid-fares");

    [Theory]
    [InlineData("Plan 5 days in Rome for two, from Beirut, under $1,200", "Plan 5 days in Rome for two, from")]
    [InlineData("Hotels near the centre?", "Hotels near the centre")]
    [InlineData("   ", "New chat")]
    [InlineData("Supercalifragilisticexpialidocious-and-then-some-more-letters", "Supercalifragilisticexpialidocious-and-t")]
    public void The_title_is_the_first_message_cut_at_a_word(string text, string title)
    {
        Assert.Equal(title, ChatTitle.From(text));
        Assert.True(ChatTitle.From(text).Length <= ChatRules.MaxTitleLength);
    }

    [Fact]
    public void Search_words_ignore_case_and_accents_and_drop_one_letter_words()
    {
        Assert.Equal<string>(["cafe", "creme", "in", "rome"], [.. ChatTerms.Words("Café crème in ROME, a must!").Where(w => w is not "must").Order(StringComparer.Ordinal)]);
        Assert.DoesNotContain("a", ChatTerms.Words("a b c"), StringComparer.Ordinal);
    }

    [Fact]
    public void A_chat_counts_its_messages_and_expires_90_days_after_the_last()
    {
        var chat = Chat.Start("c1", Fares, "Jazz in Lisbon", Now);
        var later = chat.With(ChatEntry.FromTraveller(1, "Any bars near Alfama?", null, Now.AddDays(10)));

        Assert.Equal(1, later.MessageCount);
        Assert.Equal(Now.AddDays(100), later.ExpiresAt);
        Assert.False(later.IsExpired(Now.AddDays(99)));
        Assert.True(later.IsExpired(Now.AddDays(100)));
        Assert.Contains("alfama", later.Terms, StringComparer.Ordinal);
        Assert.Contains("jazz", later.Terms, StringComparer.Ordinal);
    }

    [Fact]
    public void A_chat_is_full_at_200_messages_and_busy_while_its_lease_runs()
    {
        var chat = Chat.Start("c1", Fares, "Hi", Now) with { MessageCount = ChatRules.MaxMessages, BusyUntil = Now.AddSeconds(30) };

        Assert.True(chat.IsFull);
        Assert.True(chat.IsBusy(Now));
        Assert.False(chat.IsBusy(Now.AddSeconds(31)));
    }

    [Fact]
    public void Rename_and_messages_name_the_field_at_fault()
    {
        var chat = Chat.Start("c1", Fares, "Hi", Now);

        Assert.Equal("Rome with Maya", chat.Rename("  Rome with Maya ").Title);
        Assert.Equal("title", Assert.Throws<DomainException>(() => chat.Rename(new string('x', 41))).Field);
        Assert.Equal("text", Assert.Throws<DomainException>(() => ChatEntry.FromTraveller(1, " ", null, Now)).Field);
        Assert.Equal("text", Assert.Throws<DomainException>(() => ChatEntry.FromTraveller(1, new string('x', 1001), null, Now)).Field);
        Assert.Equal("0042", ChatEntry.FromTraveller(42, "Hi", null, Now).Id);
    }

    [Fact]
    public void About_needs_a_plan_and_an_item_needs_its_day()
    {
        Assert.Null(MessageAbout.Create(null, null, null));
        Assert.Equal(new MessageAbout("p1", 2, "wd:Q1"), MessageAbout.Create(" p1 ", 2, "wd:Q1"));
        Assert.Equal("about.day", Assert.Throws<DomainException>(() => MessageAbout.Create("p1", null, "wd:Q1")).Field);
        Assert.Equal("about.planId", Assert.Throws<DomainException>(() => MessageAbout.Create(null, 2, null)).Field);
    }
}
