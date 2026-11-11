using Pamplemousse.Domain.Assistant;
using Pamplemousse.Domain.Travellers;

namespace Pamplemousse.Application.Abstractions;

/// <summary>A page of Recents: latest message first, and the cursor of the next page (null on the last).</summary>
public sealed record ChatPage(IReadOnlyList<Chat> Items, string? Next);

/// <summary>
/// A traveller's chats and their messages (spec 010 FR-022 to FR-026; Firestore <c>travellers/{uid}/chats</c>, research R-6).
/// Every call names the owner. Expired chats are left to the caller and the sweeper. Unreachable →
/// <see cref="ServiceUnavailableException"/>.
/// </summary>
public interface IChatStore
{
    Task<Chat?> GetAsync(TravellerId owner, string id, CancellationToken ct);

    /// <summary>Oldest first.</summary>
    Task<IReadOnlyList<ChatEntry>> MessagesAsync(TravellerId owner, string id, CancellationToken ct);

    /// <summary>The last <paramref name="count"/> messages, oldest first.</summary>
    Task<IReadOnlyList<ChatEntry>> LastMessagesAsync(TravellerId owner, string id, int count, CancellationToken ct);

    Task<ChatPage> PageAsync(TravellerId owner, int limit, string? before, CancellationToken ct);

    /// <summary>Chats holding <paramref name="term"/> (a normalised word), latest message first.</summary>
    Task<IReadOnlyList<Chat>> SearchAsync(TravellerId owner, string term, CancellationToken ct);

    Task<int> CountAsync(TravellerId owner, CancellationToken ct);

    /// <summary>The chat whose last message is the oldest, for the 500-chat cap (FR-022a).</summary>
    Task<Chat?> OldestAsync(TravellerId owner, CancellationToken ct);

    /// <summary>Writes the chat and its first message together.</summary>
    Task CreateAsync(Chat chat, ChatEntry first, CancellationToken ct);

    /// <summary>Writes the chat's header (title, trip, counts, terms, lease) and, with one, a message, together.</summary>
    Task SaveAsync(Chat chat, ChatEntry? message, CancellationToken ct);

    /// <summary>Holds the chat for one answer (research R-3); false when another answer holds it.</summary>
    Task<bool> TryTakeLeaseAsync(TravellerId owner, string id, DateTimeOffset now, DateTimeOffset until, CancellationToken ct);

    /// <summary>Deletes the chat with its messages; false when there was none.</summary>
    Task<bool> DeleteAsync(TravellerId owner, string id, CancellationToken ct);

    Task DeleteAllAsync(TravellerId owner, CancellationToken ct);

    /// <summary>Deletes every chat of every account whose last message is before <paramref name="before"/>; how many.</summary>
    Task<int> DeleteExpiredAsync(DateTimeOffset before, CancellationToken ct);
}
