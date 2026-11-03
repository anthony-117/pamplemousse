using Google.Cloud.Firestore;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Hotels;

namespace Pamplemousse.Infrastructure.Storage;

/// <summary>Hotels, city mappings and price history in Firestore (ADR 0017, data-model.md).</summary>
public sealed class FirestoreHotelData(FirestoreDb db) : ICityMappingStore, IHotelStore, IPriceHistory
{
    // Firestore allows 500 writes per batch.
    private const int BatchSize = 450;

    private CollectionReference Cities => db.Collection("cities");

    private CollectionReference Hotels => db.Collection("hotels");

    private CollectionReference Snapshots => db.Collection("priceSnapshots");

    public async Task<CityMapping?> GetAsync(string destinationId, CancellationToken ct)
    {
        var doc = await Cities.Document(destinationId).GetSnapshotAsync(ct);
        return doc.Exists ? FirestoreDocuments.ToCity(destinationId, doc.ToDictionary()) : null;
    }

    public Task SaveAsync(CityMapping mapping, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        return Cities.Document(mapping.DestinationId).SetAsync(FirestoreDocuments.FromCity(mapping), cancellationToken: ct);
    }

    async Task<Hotel?> IHotelStore.GetAsync(string id, CancellationToken ct)
    {
        var doc = await Hotels.Document(FirestoreDocuments.DocumentId(id)).GetSnapshotAsync(ct);
        return doc.Exists ? FirestoreDocuments.ToHotel(doc.Id, doc.ToDictionary()) : null;
    }

    public async Task<IReadOnlyList<Hotel>> GetManyAsync(IReadOnlyCollection<string> ids, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return [];
        }

        var docs = await db.GetAllSnapshotsAsync(ids.Select(id => Hotels.Document(FirestoreDocuments.DocumentId(id))), ct);
        return [.. docs.Where(d => d.Exists).Select(d => FirestoreDocuments.ToHotel(d.Id, d.ToDictionary()))];
    }

    public async Task SaveManyAsync(IReadOnlyList<Hotel> hotels, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(hotels);
        foreach (var chunk in hotels.Chunk(BatchSize))
        {
            var batch = db.StartBatch();
            foreach (var hotel in chunk)
            {
                batch.Set(Hotels.Document(FirestoreDocuments.DocumentId(hotel.Id)), FirestoreDocuments.FromHotel(hotel));
            }

            await batch.CommitAsync(ct);
        }
    }

    public async Task AddAsync(PriceSnapshot snapshot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var historyId = FirestoreDocuments.HistoryId(snapshot.FetchedAt);
        foreach (var chunk in snapshot.Prices.Chunk(BatchSize))
        {
            var batch = db.StartBatch();
            foreach (var price in chunk)
            {
                var history = Hotels.Document(FirestoreDocuments.DocumentId(price.HotelId)).Collection("prices").Document(historyId);
                batch.Set(history, FirestoreDocuments.FromHistory(snapshot, price));
            }

            await batch.CommitAsync(ct);
        }

        await Snapshots.AddAsync(FirestoreDocuments.FromSnapshot(snapshot), ct);
    }

    public async Task<IReadOnlyList<PriceSnapshot>> GetLatestAsync(string searchKey, CancellationToken ct)
    {
        // An equality filter alone needs no composite index; a search has only a few snapshots (one per 12 hours), so they are sorted here.
        var docs = await Snapshots.WhereEqualTo("searchKey", searchKey).GetSnapshotAsync(ct);
        return [.. docs.Documents
            .Select(d => FirestoreDocuments.ToSnapshot(d.ToDictionary()))
            .GroupBy(s => s.Page)
            .Select(g => g.MaxBy(s => s.FetchedAt)!)
            .OrderBy(s => s.Page)];
    }
}
