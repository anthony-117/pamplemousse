using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Google.Cloud.Firestore;
using Grpc.Core;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Assistant;
using Pamplemousse.Domain.Itinerary;
using Pamplemousse.Domain.Ranking;
using Pamplemousse.Domain.Travellers;

namespace Pamplemousse.Infrastructure.Storage;

/// <summary>
/// Chats in Firestore (spec 010, research R-6): the header in <c>travellers/{uid}/chats/{chatId}</c> and each message in
/// <c>…/messages/{0001}</c>, its steps and cards packed as gzip JSON in <c>body</c>, as plans are. Search uses the header's
/// <c>terms</c> with <c>array-contains</c> and sorts in memory (at most 500 chats), so no composite index is needed.
/// </summary>
public sealed class FirestoreChatStore(FirestoreDb db) : IChatStore
{
    private static readonly JsonSerializerOptions Json = CreateJson();

    public Task<Chat?> GetAsync(TravellerId owner, string id, CancellationToken ct) => Guard(async () =>
    {
        var doc = await Chats(owner).Document(id).GetSnapshotAsync(ct);
        return doc.Exists ? ToChat(owner, doc) : null;
    });

    public Task<IReadOnlyList<ChatEntry>> MessagesAsync(TravellerId owner, string id, CancellationToken ct) => Guard(async () =>
    {
        var docs = await Messages(owner, id).OrderBy(FieldPath.DocumentId).GetSnapshotAsync(ct);
        return (IReadOnlyList<ChatEntry>)[.. docs.Documents.Select(Unpack)];
    });

    public Task<IReadOnlyList<ChatEntry>> LastMessagesAsync(TravellerId owner, string id, int count, CancellationToken ct) => Guard(async () =>
    {
        // By time, not id: a descending order on the document id needs an index Firestore doesn't make by itself.
        var docs = await Messages(owner, id).OrderByDescending("at").Limit(count).GetSnapshotAsync(ct);
        return (IReadOnlyList<ChatEntry>)[.. docs.Documents.Select(Unpack).OrderBy(m => m.Id, StringComparer.Ordinal)];
    });

    public Task<ChatPage> PageAsync(TravellerId owner, int limit, string? before, CancellationToken ct) => Guard(async () =>
    {
        Query query = Chats(owner).OrderByDescending("lastMessageAt");
        if (before is not null && DateTimeOffset.TryParse(before, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var cursor))
        {
            query = query.StartAfter(Timestamp.FromDateTimeOffset(cursor));
        }

        var docs = (await query.Limit(limit + 1).GetSnapshotAsync(ct)).Documents.Select(d => ToChat(owner, d)).ToList();
        var next = docs.Count > limit ? docs[limit - 1].LastMessageAt.ToString("o", CultureInfo.InvariantCulture) : null;
        return new ChatPage([.. docs.Take(limit)], next);
    });

    public Task<IReadOnlyList<Chat>> SearchAsync(TravellerId owner, string term, CancellationToken ct) => Guard(async () =>
    {
        var docs = await Chats(owner).WhereArrayContains("terms", term).GetSnapshotAsync(ct);
        return (IReadOnlyList<Chat>)[.. docs.Documents.Select(d => ToChat(owner, d)).OrderByDescending(c => c.LastMessageAt)];
    });

    public Task<int> CountAsync(TravellerId owner, CancellationToken ct) => Guard(async () =>
        (int)((await Chats(owner).Count().GetSnapshotAsync(ct)).Count ?? 0));

    public Task<Chat?> OldestAsync(TravellerId owner, CancellationToken ct) => Guard(async () =>
    {
        var docs = await Chats(owner).OrderBy("lastMessageAt").Limit(1).GetSnapshotAsync(ct);
        return docs.Count == 0 ? null : ToChat(owner, docs.Documents[0]);
    });

    public Task CreateAsync(Chat chat, ChatEntry first, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(first);
        return Guard(async () =>
        {
            var batch = db.StartBatch();
            var doc = Chats(chat.Owner).Document(chat.Id);
            batch.Create(doc, ToDocument(chat));
            batch.Set(Owners.Document(chat.Owner.Value), new Dictionary<string, object>(StringComparer.Ordinal) { ["since"] = Timestamp.FromDateTimeOffset(chat.CreatedAt) }, SetOptions.MergeAll);
            batch.Set(doc.Collection("messages").Document(first.Id), Pack(first));
            await batch.CommitAsync(ct);
        });
    }

    public Task SaveAsync(Chat chat, ChatEntry? message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(chat);
        return Guard(async () =>
        {
            var doc = Chats(chat.Owner).Document(chat.Id);
            if (!(await doc.GetSnapshotAsync(ct)).Exists)
            {
                // Deleted while an answer ran: nothing to save it to.
                return;
            }

            var batch = db.StartBatch();
            batch.Set(doc, ToDocument(chat), SetOptions.Overwrite);
            if (message is not null)
            {
                batch.Set(doc.Collection("messages").Document(message.Id), Pack(message));
            }

            await batch.CommitAsync(ct);
        });
    }

    public Task<bool> TryTakeLeaseAsync(TravellerId owner, string id, DateTimeOffset now, DateTimeOffset until, CancellationToken ct) => Guard(() =>
        db.RunTransactionAsync(
            async tx =>
            {
                var doc = Chats(owner).Document(id);
                var snapshot = await tx.GetSnapshotAsync(doc, ct);
                if (!snapshot.Exists || (snapshot.TryGetValue<Timestamp?>("busyUntil", out var busy) && busy is { } b && b.ToDateTimeOffset() > now))
                {
                    return false;
                }

                tx.Update(doc, "busyUntil", Timestamp.FromDateTimeOffset(until));
                return true;
            },
            cancellationToken: ct));

    public Task<bool> DeleteAsync(TravellerId owner, string id, CancellationToken ct) => Guard(async () =>
    {
        var doc = Chats(owner).Document(id);
        if (!(await doc.GetSnapshotAsync(ct)).Exists)
        {
            return false;
        }

        await DeleteChatAsync(doc, ct);
        return true;
    });

    public Task DeleteAllAsync(TravellerId owner, CancellationToken ct) => Guard(async () =>
    {
        var docs = await Chats(owner).Select(FieldPath.DocumentId).GetSnapshotAsync(ct);
        foreach (var doc in docs.Documents)
        {
            await DeleteChatAsync(doc.Reference, ct);
        }

        await Owners.Document(owner.Value).DeleteAsync(cancellationToken: ct);
    });

    /// <summary>
    /// Every traveller's chats with a last message before <paramref name="before"/> (research R-8). The travellers with
    /// chats are listed in <c>chatOwners</c>, written with each new chat, so the sweep needs only ordinary queries: no
    /// admin listing and no collection-group index. A traveller left with no chat leaves the list.
    /// </summary>
    public Task<int> DeleteExpiredAsync(DateTimeOffset before, CancellationToken ct) => Guard(async () =>
    {
        var deleted = 0;
        var owners = await Owners.Select(FieldPath.DocumentId).GetSnapshotAsync(ct);
        foreach (var owner in owners.Documents)
        {
            var chats = db.Collection("travellers").Document(owner.Id).Collection("chats");
            var old = await chats.WhereLessThan("lastMessageAt", Timestamp.FromDateTimeOffset(before)).Select(FieldPath.DocumentId).GetSnapshotAsync(ct);
            foreach (var doc in old.Documents)
            {
                await DeleteChatAsync(doc.Reference, ct);
                deleted++;
            }

            if ((await chats.Count().GetSnapshotAsync(ct)).Count == 0)
            {
                await owner.Reference.DeleteAsync(cancellationToken: ct);
            }
        }

        return deleted;
    });

    /// <summary>The travellers who have chats: <c>chatOwners/{uid}</c>.</summary>
    private CollectionReference Owners => db.Collection("chatOwners");

    internal static Dictionary<string, object?> ToDocument(Chat chat) => new(StringComparer.Ordinal)
    {
        ["title"] = chat.Title,
        ["trip"] = chat.Trip is { } t
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["destinationId"] = t.DestinationId,
                ["from"] = t.From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["to"] = t.To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["adults"] = t.Adults,
                ["originId"] = t.OriginId,
                ["passport"] = t.Passport,
                ["currency"] = t.Currency,
                ["tripBudget"] = t.TripBudget is { } b ? (double)b : null,
            }
            : null,
        ["preferences"] = chat.Preferences is { } w
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["budget"] = w.Budget,
                ["culture"] = w.Culture,
                ["food"] = w.Food,
                ["nature"] = w.Nature,
                ["nightlife"] = w.Nightlife,
                ["comfort"] = w.Comfort,
            }
            : null,
        ["pace"] = chat.Pace?.ToString().ToLowerInvariant(),
        ["createdAt"] = Timestamp.FromDateTimeOffset(chat.CreatedAt),
        ["lastMessageAt"] = Timestamp.FromDateTimeOffset(chat.LastMessageAt),
        ["messageCount"] = chat.MessageCount,
        ["busyUntil"] = chat.BusyUntil is { } until ? Timestamp.FromDateTimeOffset(until) : null,
        ["terms"] = chat.Terms.ToList(),
    };

    internal static Chat ToChat(TravellerId owner, DocumentSnapshot doc)
    {
        var d = doc.ToDictionary();
        string? Str(IDictionary<string, object> m, string key) => m.TryGetValue(key, out var v) ? v as string : null;
        int? Int(IDictionary<string, object> m, string key) => m.TryGetValue(key, out var v) && v is long l ? (int)l : null;
        DateTimeOffset Time(string key) => d.TryGetValue(key, out var v) && v is Timestamp t ? t.ToDateTimeOffset() : DateTimeOffset.MinValue;
        DateOnly? Day(IDictionary<string, object> m, string key) =>
            DateOnly.TryParseExact(Str(m, key), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;

        TripContext? trip = null;
        if (d.TryGetValue("trip", out var tripValue) && tripValue is Dictionary<string, object> t)
        {
            trip = new TripContext(
                Str(t, "destinationId"),
                Day(t, "from"),
                Day(t, "to"),
                Int(t, "adults"),
                Str(t, "originId"),
                Str(t, "passport"),
                Str(t, "currency"),
                t.TryGetValue("tripBudget", out var budget) && budget is double b ? (decimal)b : null);
        }

        Weights? weights = null;
        if (d.TryGetValue("preferences", out var prefValue) && prefValue is Dictionary<string, object> p)
        {
            weights = new Weights(
                Int(p, "budget") ?? Weights.Default,
                Int(p, "culture") ?? Weights.Default,
                Int(p, "food") ?? Weights.Default,
                Int(p, "nature") ?? Weights.Default,
                Int(p, "nightlife") ?? Weights.Default,
                Int(p, "comfort") ?? Weights.Default);
        }

        return new Chat(
            doc.Id,
            owner,
            Str(d, "title") ?? "New chat",
            trip,
            weights,
            Enum.TryParse<Pace>(Str(d, "pace"), ignoreCase: true, out var pace) ? pace : null,
            Time("createdAt"),
            Time("lastMessageAt"),
            Int(d, "messageCount") ?? 0,
            d.TryGetValue("busyUntil", out var busy) && busy is Timestamp until ? until.ToDateTimeOffset() : null,
            d.TryGetValue("terms", out var terms) && terms is List<object> words ? [.. words.OfType<string>()] : []);
    }

    private static Dictionary<string, object?> Pack(ChatEntry message)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        {
            JsonSerializer.Serialize(gzip, message, Json);
        }

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["role"] = message.Role.ToString().ToLowerInvariant(),
            ["at"] = Timestamp.FromDateTimeOffset(message.At),
            ["body"] = Blob.CopyFrom(output.ToArray()),
        };
    }

    private static ChatEntry Unpack(DocumentSnapshot doc)
    {
        var body = doc.GetValue<Blob>("body").ByteString.ToByteArray();
        using var gzip = new GZipStream(new MemoryStream(body), CompressionMode.Decompress);
        return JsonSerializer.Deserialize<ChatEntry>(gzip, Json) ?? throw new JsonException("An empty message was stored.");
    }

    private async Task DeleteChatAsync(DocumentReference chat, CancellationToken ct)
    {
        // At most 200 messages and the chat: one batch.
        var messages = await chat.Collection("messages").Select(FieldPath.DocumentId).GetSnapshotAsync(ct);
        var batch = db.StartBatch();
        foreach (var message in messages.Documents)
        {
            batch.Delete(message.Reference);
        }

        batch.Delete(chat);
        await batch.CommitAsync(ct);
    }

    private CollectionReference Chats(TravellerId owner) => db.Collection("travellers").Document(owner.Value).Collection("chats");

    private CollectionReference Messages(TravellerId owner, string id) => Chats(owner).Document(id).Collection("messages");

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerOptions.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static async Task Guard(Func<Task> work) => await Guard(async () =>
    {
        await work();
        return true;
    });

    private static async Task<T> Guard<T>(Func<Task<T>> work)
    {
        try
        {
            return await work();
        }
        catch (RpcException ex)
        {
            throw new ServiceUnavailableException("Chats can't be reached.", ex);
        }
    }
}
