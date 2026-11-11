using System.Collections.Concurrent;
using Google.Cloud.Firestore;
using Grpc.Core;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Saved;
using Pamplemousse.Domain.Travellers;

namespace Pamplemousse.Infrastructure.Storage;

/// <summary>
/// Saved items in Firestore, one <c>travellers/{uid}/saved/{kind}:{id}</c> document each (spec 010, data-model.md). The id
/// is escaped, since an item id may hold a slash.
/// </summary>
public sealed class FirestoreSavedStore(FirestoreDb db) : ISavedStore
{
    public Task<SavedItem?> GetAsync(TravellerId owner, SavedKind kind, string id, CancellationToken ct) => Guard(async () =>
    {
        var doc = await Saved(owner).Document(Key(kind, id)).GetSnapshotAsync(ct);
        return doc.Exists ? ToItem(owner, doc.ToDictionary()) : null;
    });

    public Task<int> CountAsync(TravellerId owner, CancellationToken ct) => Guard(async () =>
        (int)((await Saved(owner).Count().GetSnapshotAsync(ct)).Count ?? 0));

    public Task PutAsync(SavedItem item, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(item);
        return Guard(() => Saved(item.Owner).Document(Key(item.Kind, item.Id)).SetAsync(ToDocument(item), SetOptions.Overwrite, ct));
    }

    public Task DeleteAsync(TravellerId owner, SavedKind kind, string id, CancellationToken ct) =>
        Guard(() => Saved(owner).Document(Key(kind, id)).DeleteAsync(cancellationToken: ct));

    public Task<IReadOnlyList<SavedItem>> ListAsync(TravellerId owner, CancellationToken ct) => Guard(async () =>
    {
        var docs = await Saved(owner).OrderByDescending("savedAt").GetSnapshotAsync(ct);
        return (IReadOnlyList<SavedItem>)[.. docs.Documents.Select(d => ToItem(owner, d.ToDictionary())).OfType<SavedItem>()];
    });

    public Task DeleteAllAsync(TravellerId owner, CancellationToken ct) => Guard(async () =>
    {
        // At most 200 items, under a batch's 500 writes.
        var docs = await Saved(owner).Select(FieldPath.DocumentId).GetSnapshotAsync(ct);
        if (docs.Count == 0)
        {
            return;
        }

        var batch = db.StartBatch();
        foreach (var doc in docs.Documents)
        {
            batch.Delete(doc.Reference);
        }

        await batch.CommitAsync(ct);
    });

    internal static string Key(SavedKind kind, string id) => kind.ToString().ToLowerInvariant() + ":" + Uri.EscapeDataString(id);

    internal static Dictionary<string, object?> ToDocument(SavedItem item) => new(StringComparer.Ordinal)
    {
        ["kind"] = item.Kind.ToString().ToLowerInvariant(),
        ["id"] = item.Id,
        ["destinationId"] = item.DestinationId,
        ["destinationName"] = item.DestinationName,
        ["name"] = item.Name,
        ["imageUrl"] = item.ImageUrl,
        ["lat"] = item.Location.Lat,
        ["lng"] = item.Location.Lng,
        ["savedAt"] = Timestamp.FromDateTimeOffset(item.SavedAt),
        ["item"] = item.ItemJson,
    };

    internal static SavedItem? ToItem(TravellerId owner, IDictionary<string, object> d)
    {
        string? Str(string key) => d.TryGetValue(key, out var v) ? v as string : null;
        double Num(string key) => d.TryGetValue(key, out var v) ? Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture) : 0;
        if (!Enum.TryParse<SavedKind>(Str("kind"), ignoreCase: true, out var kind) || Str("id") is not { } id)
        {
            return null;
        }

        return new SavedItem(
            owner,
            kind,
            id,
            Str("destinationId") ?? "",
            Str("destinationName") ?? "",
            Str("name") ?? "",
            Str("imageUrl"),
            new Domain.Common.GeoPoint(Num("lat"), Num("lng")),
            d.TryGetValue("savedAt", out var t) && t is Timestamp time ? time.ToDateTimeOffset() : DateTimeOffset.MinValue,
            Str("item"));
    }

    private CollectionReference Saved(TravellerId owner) => db.Collection("travellers").Document(owner.Value).Collection("saved");

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
            throw new ServiceUnavailableException("Saved items can't be reached.", ex);
        }
    }
}

/// <summary>Stands in for Firestore when it is not configured: nothing survives a restart.</summary>
public sealed class InMemorySavedStore : ISavedStore
{
    private readonly ConcurrentDictionary<(string Owner, SavedKind Kind, string Id), SavedItem> _items = new();

    public Task<SavedItem?> GetAsync(TravellerId owner, SavedKind kind, string id, CancellationToken ct) =>
        Task.FromResult(_items.TryGetValue((owner.Value, kind, id), out var item) ? item : null);

    public Task<int> CountAsync(TravellerId owner, CancellationToken ct) => Task.FromResult(Mine(owner).Count());

    public Task PutAsync(SavedItem item, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items[(item.Owner.Value, item.Kind, item.Id)] = item;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(TravellerId owner, SavedKind kind, string id, CancellationToken ct)
    {
        _items.TryRemove((owner.Value, kind, id), out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SavedItem>> ListAsync(TravellerId owner, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SavedItem>>([.. Mine(owner).OrderByDescending(i => i.SavedAt).ThenBy(i => i.Id, StringComparer.Ordinal)]);

    public Task DeleteAllAsync(TravellerId owner, CancellationToken ct)
    {
        foreach (var item in Mine(owner).ToList())
        {
            _items.TryRemove((owner.Value, item.Kind, item.Id), out _);
        }

        return Task.CompletedTask;
    }

    private IEnumerable<SavedItem> Mine(TravellerId owner) =>
        _items.Values.Where(i => string.Equals(i.Owner.Value, owner.Value, StringComparison.Ordinal));
}
