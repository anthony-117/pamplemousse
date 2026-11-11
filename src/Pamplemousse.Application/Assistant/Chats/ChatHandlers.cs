using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Assistant;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Travellers;

namespace Pamplemousse.Application.Assistant.Chats;

/// <summary>Recents (FR-022): latest message first. <see cref="Limit"/> is 1–100; <see cref="Before"/> the last page's cursor.</summary>
public sealed record ListChatsQuery(TravellerId Owner, int? Limit, string? Before);

public sealed record ChatList(IReadOnlyList<Chat> Items, string? Next, int KeepDays);

/// <summary>Chats past their 90 days are left out, as if the sweeper had already run (research R-8).</summary>
public sealed class ListChatsHandler(IChatStore store, TimeProvider clock) : IQueryHandler<ListChatsQuery, ChatList>
{
    public async Task<ChatList> HandleAsync(ListChatsQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var limit = query.Limit ?? 30;
        if (limit is < 1 or > 100)
        {
            throw new DomainException("Use a limit of 1 to 100.", "limit");
        }

        var now = clock.GetUtcNow();
        var page = await store.PageAsync(query.Owner, limit, string.IsNullOrWhiteSpace(query.Before) ? null : query.Before, ct);
        return new ChatList([.. page.Items.Where(c => !c.IsExpired(now))], page.Next, ChatRules.KeepDays);
    }
}

public sealed record GetChatQuery(TravellerId Owner, string Id);

/// <summary>The chat with every message, oldest first (FR-023).</summary>
public sealed record ChatWithMessages(Chat Chat, IReadOnlyList<ChatEntry> Messages, bool Busy);

public sealed class GetChatHandler(IChatStore store, TimeProvider clock) : IQueryHandler<GetChatQuery, ChatWithMessages>
{
    public async Task<ChatWithMessages> HandleAsync(GetChatQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var now = clock.GetUtcNow();
        var chat = await ChatLookup.FindAsync(store, query.Owner, query.Id, now, ct);
        return new ChatWithMessages(chat, await store.MessagesAsync(query.Owner, chat.Id, ct), chat.IsBusy(now));
    }
}

public sealed record RenameChatCommand(TravellerId Owner, string Id, string? Title);

public sealed class RenameChatHandler(IChatStore store, TimeProvider clock) : ICommandHandler<RenameChatCommand, Chat>
{
    public async Task<Chat> HandleAsync(RenameChatCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var chat = (await ChatLookup.FindAsync(store, command.Owner, command.Id, clock.GetUtcNow(), ct)).Rename(command.Title);
        await store.SaveAsync(chat, null, ct);
        return chat;
    }
}

public sealed record DeleteChatCommand(TravellerId Owner, string Id);

/// <summary>Deletes the chat and its messages; plans built in it stay (FR-022, FR-026).</summary>
public sealed class DeleteChatHandler(IChatStore store) : ICommandHandler<DeleteChatCommand, bool>
{
    public async Task<bool> HandleAsync(DeleteChatCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        return !string.IsNullOrWhiteSpace(command.Id) && await store.DeleteAsync(command.Owner, command.Id.Trim(), ct)
            ? true
            : throw new NotFoundException("The chat was not found.");
    }
}

/// <summary>Search chats (FR-022b, FR-028): <see cref="Q"/> is 2–100 characters.</summary>
public sealed record SearchChatsQuery(TravellerId Owner, string? Q);

public sealed record ChatSearch(IReadOnlyList<Chat> Chats, IReadOnlyList<PlanSummary> Plans);

/// <summary>
/// Whole words, case and accents ignored, every word of the query present (research R-7): the store finds the chats
/// holding the longest word, and the rest are checked here. Plans match when their name holds the query.
/// </summary>
public sealed class SearchChatsHandler(IChatStore store, IPlanStore plans, TimeProvider clock) : IQueryHandler<SearchChatsQuery, ChatSearch>
{
    public async Task<ChatSearch> HandleAsync(SearchChatsQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var q = query.Q?.Trim() ?? "";
        if (q.Length is < 2 or > 100)
        {
            throw new DomainException("Search with 2 to 100 characters.", "q");
        }

        var words = ChatTerms.Words(q).ToList();
        var now = clock.GetUtcNow();
        IReadOnlyList<Chat> chats = [];
        if (words.Count > 0)
        {
            var found = await store.SearchAsync(query.Owner, words.MaxBy(w => w.Length)!, ct);
            chats = [.. found.Where(c => !c.IsExpired(now) && words.All(w => c.Terms.Contains(w, StringComparer.Ordinal)))];
        }

        var needle = string.Join(' ', words);
        var matching = (await plans.ListAsync(query.Owner, ct))
            .Where(p => needle.Length > 0 && string.Join(' ', ChatTerms.Words(p.Name)).Contains(needle, StringComparison.Ordinal))
            .ToList();
        return new ChatSearch(chats, matching);
    }
}

internal static class ChatLookup
{
    /// <summary>The owner's chat; another traveller's, or one past its 90 days, is not found (FR-026, FR-033).</summary>
    public static async Task<Chat> FindAsync(IChatStore store, TravellerId owner, string id, DateTimeOffset now, CancellationToken ct) =>
        (string.IsNullOrWhiteSpace(id) ? null : await store.GetAsync(owner, id.Trim(), ct)) is { } chat && !chat.IsExpired(now)
            ? chat
            : throw new NotFoundException("The chat was not found.");
}
