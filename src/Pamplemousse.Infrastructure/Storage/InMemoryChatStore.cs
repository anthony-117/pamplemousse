using System.Collections.Concurrent;
using System.Globalization;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Assistant;
using Pamplemousse.Domain.Travellers;

namespace Pamplemousse.Infrastructure.Storage;

/// <summary>Stands in for Firestore when it is not configured: everything works, but nothing survives a restart.</summary>
public sealed class InMemoryChatStore : IChatStore
{
    private readonly ConcurrentDictionary<(string Owner, string Id), Entry> _chats = new();

    private sealed class Entry(Chat chat)
    {
        public Chat Chat { get; set; } = chat;

        public List<ChatEntry> Messages { get; } = [];

        public Lock Lock { get; } = new();
    }

    public Task<Chat?> GetAsync(TravellerId owner, string id, CancellationToken ct) =>
        Task.FromResult(_chats.TryGetValue((owner.Value, id), out var e) ? e.Chat : null);

    public Task<IReadOnlyList<ChatEntry>> MessagesAsync(TravellerId owner, string id, CancellationToken ct) =>
        LastMessagesAsync(owner, id, int.MaxValue, ct);

    public Task<IReadOnlyList<ChatEntry>> LastMessagesAsync(TravellerId owner, string id, int count, CancellationToken ct)
    {
        if (!_chats.TryGetValue((owner.Value, id), out var e))
        {
            return Task.FromResult<IReadOnlyList<ChatEntry>>([]);
        }

        lock (e.Lock)
        {
            return Task.FromResult<IReadOnlyList<ChatEntry>>([.. e.Messages.Skip(Math.Max(0, e.Messages.Count - count))]);
        }
    }

    public Task<ChatPage> PageAsync(TravellerId owner, int limit, string? before, CancellationToken ct)
    {
        var cursor = before is null ? (DateTimeOffset?)null : DateTimeOffset.Parse(before, CultureInfo.InvariantCulture);
        var page = Mine(owner)
            .Where(c => cursor is null || c.LastMessageAt < cursor)
            .Take(limit + 1)
            .ToList();
        var next = page.Count > limit ? page[limit - 1].LastMessageAt.ToString("o", CultureInfo.InvariantCulture) : null;
        return Task.FromResult(new ChatPage([.. page.Take(limit)], next));
    }

    public Task<IReadOnlyList<Chat>> SearchAsync(TravellerId owner, string term, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Chat>>([.. Mine(owner).Where(c => c.Terms.Contains(term, StringComparer.Ordinal))]);

    public Task<int> CountAsync(TravellerId owner, CancellationToken ct) => Task.FromResult(Mine(owner).Count());

    public Task<Chat?> OldestAsync(TravellerId owner, CancellationToken ct) => Task.FromResult(Mine(owner).LastOrDefault());

    public Task CreateAsync(Chat chat, ChatEntry first, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(chat);
        var entry = new Entry(chat);
        entry.Messages.Add(first);
        _chats[(chat.Owner.Value, chat.Id)] = entry;
        return Task.CompletedTask;
    }

    public Task SaveAsync(Chat chat, ChatEntry? message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(chat);
        if (!_chats.TryGetValue((chat.Owner.Value, chat.Id), out var e))
        {
            // Deleted while an answer ran: nothing to save it to.
            return Task.CompletedTask;
        }

        lock (e.Lock)
        {
            e.Chat = chat;
            if (message is not null)
            {
                e.Messages.Add(message);
            }
        }

        return Task.CompletedTask;
    }

    public Task<bool> TryTakeLeaseAsync(TravellerId owner, string id, DateTimeOffset now, DateTimeOffset until, CancellationToken ct)
    {
        if (!_chats.TryGetValue((owner.Value, id), out var e))
        {
            return Task.FromResult(false);
        }

        lock (e.Lock)
        {
            if (e.Chat.IsBusy(now))
            {
                return Task.FromResult(false);
            }

            e.Chat = e.Chat with { BusyUntil = until };
            return Task.FromResult(true);
        }
    }

    public Task<bool> DeleteAsync(TravellerId owner, string id, CancellationToken ct) => Task.FromResult(_chats.TryRemove((owner.Value, id), out _));

    public Task DeleteAllAsync(TravellerId owner, CancellationToken ct)
    {
        foreach (var chat in Mine(owner).ToList())
        {
            _chats.TryRemove((owner.Value, chat.Id), out _);
        }

        return Task.CompletedTask;
    }

    public Task<int> DeleteExpiredAsync(DateTimeOffset before, CancellationToken ct)
    {
        var old = _chats.Where(c => c.Value.Chat.LastMessageAt < before).Select(c => c.Key).ToList();
        foreach (var key in old)
        {
            _chats.TryRemove(key, out _);
        }

        return Task.FromResult(old.Count);
    }

    /// <summary>Latest message first.</summary>
    private IEnumerable<Chat> Mine(TravellerId owner) => _chats
        .Where(c => string.Equals(c.Key.Owner, owner.Value, StringComparison.Ordinal))
        .Select(c => c.Value.Chat)
        .OrderByDescending(c => c.LastMessageAt)
        .ThenBy(c => c.Id, StringComparer.Ordinal);
}
