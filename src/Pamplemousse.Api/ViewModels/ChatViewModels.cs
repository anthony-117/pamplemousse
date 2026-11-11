using System.Text.Json;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Assistant.Chats;
using Pamplemousse.Domain.Assistant;
using Pamplemousse.Domain.Ranking;

namespace Pamplemousse.Api.ViewModels;

/// <summary>The chat's trip as the traveller set it (contracts/chat-api.md); every value optional.</summary>
public sealed record ChatTripViewModel(
    string? DestinationId,
    DateOnly? From,
    DateOnly? To,
    int? Adults,
    string? OriginId,
    string? Passport,
    string? Currency,
    decimal? TripBudget)
{
    public static ChatTripViewModel? Of(TripContext? t) =>
        t is null || t.IsEmpty ? null : new(t.DestinationId, t.From, t.To, t.Adults, t.OriginId, t.Passport, t.Currency, t.TripBudget);
}

/// <summary>A line of Recents.</summary>
public sealed record ChatListItemViewModel(string Id, string Title, ChatTripViewModel? Trip, DateTimeOffset CreatedAt, DateTimeOffset LastMessageAt, int MessageCount)
{
    public static ChatListItemViewModel Of(Chat c)
    {
        ArgumentNullException.ThrowIfNull(c);
        return new(c.Id, c.Title, ChatTripViewModel.Of(c.Trip), c.CreatedAt, c.LastMessageAt, c.MessageCount);
    }
}

/// <summary>A page of Recents; <see cref="Next"/> is the cursor of the next page.</summary>
public sealed record ChatPageViewModel(IReadOnlyList<ChatListItemViewModel> Items, string? Next, int KeepDays)
{
    public static ChatPageViewModel Of(ChatList list)
    {
        ArgumentNullException.ThrowIfNull(list);
        return new([.. list.Items.Select(ChatListItemViewModel.Of)], list.Next, list.KeepDays);
    }
}

public sealed record MessageAboutViewModel(string PlanId, int? Day, string? ItemId);

/// <summary>One tool the assistant ran: the server's summary and the cards it produced.</summary>
public sealed record ToolStepViewModel(string Tool, string Summary, bool Ok, int? Total, IReadOnlyList<JsonElement> Cards)
{
    public static ToolStepViewModel Of(ToolStep s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new(s.Tool, s.Summary, s.Ok, s.Total, ChatJson.Cards(s.Cards));
    }
}

public sealed record ChatMessageViewModel(
    string Id,
    Speaker Role,
    string Text,
    MessageAboutViewModel? About,
    IReadOnlyList<ToolStepViewModel> Steps,
    IReadOnlyList<JsonElement> Cards,
    IReadOnlyList<string> Suggestions,
    IReadOnlyList<string> Warnings,
    string? PlanChanged,
    DateTimeOffset At)
{
    public static ChatMessageViewModel Of(ChatEntry m)
    {
        ArgumentNullException.ThrowIfNull(m);
        return new(
            m.Id,
            m.Role,
            m.Text,
            m.About is { } a ? new MessageAboutViewModel(a.PlanId, a.Day, a.ItemId) : null,
            [.. m.Steps.Select(ToolStepViewModel.Of)],
            ChatJson.Cards(m.Cards),
            m.Suggestions,
            m.Warnings,
            m.PlanChanged,
            m.At);
    }
}

/// <summary>A chat and every message, oldest first. <see cref="Busy"/>: an answer is still running on another device.</summary>
public sealed record ChatViewModel(
    string Id,
    string Title,
    ChatTripViewModel? Trip,
    IReadOnlyDictionary<string, int>? Preferences,
    string? Pace,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastMessageAt,
    DateTimeOffset ExpiresAt,
    bool Busy,
    IReadOnlyList<ChatMessageViewModel> Messages)
{
    public static ChatViewModel Of(ChatWithMessages c)
    {
        ArgumentNullException.ThrowIfNull(c);
        var chat = c.Chat;
        return new(
            chat.Id,
            chat.Title,
            ChatTripViewModel.Of(chat.Trip),
            ChatJson.Weights(chat.Preferences),
            chat.Pace?.ToString().ToLowerInvariant(),
            chat.CreatedAt,
            chat.LastMessageAt,
            chat.ExpiresAt,
            c.Busy,
            [.. c.Messages.Select(ChatMessageViewModel.Of)]);
    }
}

/// <summary>Search chats: matching chats, then matching plans.</summary>
public sealed record ChatSearchViewModel(IReadOnlyList<ChatListItemViewModel> Chats, IReadOnlyList<PlanSummaryViewModel> Plans);

internal static class ChatJson
{
    public static IReadOnlyList<JsonElement> Cards(IReadOnlyList<string> cards) => [.. cards.Select(Parse)];

    public static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    public static IReadOnlyDictionary<string, int>? Weights(Weights? w) => w is null
        ? null
        : new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["budget"] = w.Budget,
            ["culture"] = w.Culture,
            ["food"] = w.Food,
            ["nature"] = w.Nature,
            ["nightlife"] = w.Nightlife,
            ["comfort"] = w.Comfort,
        };

    public static PlanSummaryViewModel Plan(PlanSummary s) => Mappings.PlanMappings.ToViewModel(s);
}
