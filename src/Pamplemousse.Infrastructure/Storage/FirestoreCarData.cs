using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using Google.Cloud.Firestore;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Cars;
using DomainGeoPoint = Pamplemousse.Domain.Common.GeoPoint;
using FirestoreGeoPoint = Google.Cloud.Firestore.GeoPoint;

namespace Pamplemousse.Infrastructure.Storage;

/// <summary>
/// Car airports and car search history in Firestore (spec 004, data-model.md). Airports sit on the city's document, merged so
/// the hotel fields stay. Each search is one <c>carSnapshots</c> document, never changed: its cars are gzip-compressed JSON,
/// since 500 cars as fields would pass Firestore's index-entry limit, and the source's whole answer is kept gzip-compressed too.
/// </summary>
public sealed class FirestoreCarData(FirestoreDb db) : ICarAirportStore, ICarHistory
{
    private CollectionReference Cities => db.Collection("cities");

    private CollectionReference Snapshots => db.Collection("carSnapshots");

    public async Task<CarAirports?> GetAsync(string destinationId, CancellationToken ct)
    {
        var doc = await Cities.Document(destinationId).GetSnapshotAsync(ct);
        if (!doc.Exists || !doc.TryGetValue<Timestamp>("carAirportsCheckedAt", out var checkedAt))
        {
            return null;
        }

        var airports = doc.TryGetValue<List<Dictionary<string, object>>>("carAirports", out var list) ? list : [];
        return new CarAirports(
            destinationId,
            [.. airports
                .Where(a => a.GetValueOrDefault("iata") is string && a.GetValueOrDefault("location") is FirestoreGeoPoint)
                .Select(a =>
                {
                    var at = (FirestoreGeoPoint)a["location"];
                    var iata = (string)a["iata"];
                    return PickUpPlace.Airport(iata, a.GetValueOrDefault("name") as string ?? iata, new DomainGeoPoint(at.Latitude, at.Longitude));
                })],
            checkedAt.ToDateTimeOffset());
    }

    public Task SaveAsync(CarAirports airports, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(airports);
        var fields = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["carAirports"] = airports.Airports.Select(a => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["iata"] = a.Iata,
                ["name"] = a.Name,
                ["location"] = new FirestoreGeoPoint(a.Location.Lat, a.Location.Lng),
            }).ToList(),
            ["carAirportsCheckedAt"] = Timestamp.FromDateTimeOffset(airports.CheckedAt),
        };
        return Cities.Document(airports.DestinationId).SetAsync(fields, SetOptions.MergeAll, ct);
    }

    public Task AddAsync(CarSnapshot snapshot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var fields = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["searchKey"] = snapshot.SearchKey,
            ["destinationId"] = snapshot.DestinationId,
            ["placeId"] = snapshot.PlaceId,
            ["pickUpDate"] = Date(snapshot.From),
            ["dropOffDate"] = Date(snapshot.To),
            ["driverAge"] = snapshot.DriverAge,
            ["currency"] = snapshot.Currency,
            ["fetchedAt"] = Timestamp.FromDateTimeOffset(snapshot.FetchedAt),
            ["mayHaveMore"] = snapshot.MayHaveMore,
            ["carCount"] = snapshot.Cars.Count,
            ["cars"] = Blob.CopyFrom(CarJson.Pack(snapshot.Cars)),
            ["raw"] = Blob.CopyFrom(snapshot.Raw),
        };
        return Snapshots.Document().CreateAsync(fields, ct);
    }

    public async Task<CarSnapshot?> GetLatestAsync(string searchKey, CancellationToken ct)
    {
        // An equality filter alone needs no composite index; a search gets about one snapshot a day, so they are sorted here.
        var docs = await Snapshots.WhereEqualTo("searchKey", searchKey).GetSnapshotAsync(ct);
        var latest = docs.Documents
            .Where(d => d.TryGetValue<Timestamp>("fetchedAt", out _))
            .MaxBy(d => d.GetValue<Timestamp>("fetchedAt"));
        return latest is null ? null : ToSnapshot(latest);
    }

    private static CarSnapshot ToSnapshot(DocumentSnapshot d) => new(
        d.GetValue<string>("searchKey"),
        d.TryGetValue<string>("destinationId", out var destination) ? destination : "",
        d.TryGetValue<string>("placeId", out var place) ? place : "",
        ParseDate(d.TryGetValue<string>("pickUpDate", out var from) ? from : null),
        ParseDate(d.TryGetValue<string>("dropOffDate", out var to) ? to : null),
        d.TryGetValue<int>("driverAge", out var age) ? age : 0,
        d.TryGetValue<string>("currency", out var currency) ? currency : "",
        d.GetValue<Timestamp>("fetchedAt").ToDateTimeOffset(),
        d.TryGetValue<bool>("mayHaveMore", out var more) && more,
        d.TryGetValue<Blob>("cars", out var cars) ? CarJson.Unpack(cars.ByteString.ToByteArray()) : [],
        d.TryGetValue<Blob>("raw", out var raw) ? raw.ByteString.ToByteArray() : []);

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly ParseDate(string? text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : default;
}

/// <summary>Cars as gzip-compressed JSON: about 30 KB for 500 cars instead of about 280 KB.</summary>
internal static class CarJson
{
    public static byte[] Pack(IReadOnlyList<CarOffer> cars)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        {
            JsonSerializer.Serialize(gzip, cars, JsonSerializerOptions.Web);
        }

        return output.ToArray();
    }

    public static IReadOnlyList<CarOffer> Unpack(byte[] packed)
    {
        if (packed.Length == 0)
        {
            return [];
        }

        using var gzip = new GZipStream(new MemoryStream(packed), CompressionMode.Decompress);
        return JsonSerializer.Deserialize<List<CarOffer>>(gzip, JsonSerializerOptions.Web) ?? [];
    }
}
