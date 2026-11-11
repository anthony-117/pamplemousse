using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Google.Cloud.Firestore;
using Grpc.Core;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Itinerary;
using Pamplemousse.Domain.Travellers;

namespace Pamplemousse.Infrastructure.Storage;

/// <summary>
/// Plans in Firestore, one <c>travellers/{uid}/plans/{planId}</c> document each (spec 008, data-model.md, research R-8). The
/// list fields are written out so "My plans" reads them with a field mask; the whole plan, with its copies of every offer, is
/// gzip-compressed JSON in <c>body</c>, as cars are (spec 004), since its nested lists would pass Firestore's index limits.
/// </summary>
public sealed class FirestorePlanStore(FirestoreDb db) : IPlanStore
{
    private static readonly string[] ListFields =
        ["name", "status", "destinationName", "from", "to", "total", "currency", "matchPercent", "imageUrl", "updatedAt",
            "destinationId", "adults", "tripBudget", "pace", "lat", "lng"];

    public Task<int> CountAsync(TravellerId owner, CancellationToken ct) => Guard(async () =>
    {
        var snapshot = await Plans(owner).Count().GetSnapshotAsync(ct);
        return (int)(snapshot.Count ?? 0);
    });

    public Task AddAsync(Plan plan, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return Guard(() => Plans(plan.Owner).Document(plan.Id).CreateAsync(ToDocument(plan), ct));
    }

    public Task<Plan?> GetAsync(TravellerId owner, string id, CancellationToken ct) => Guard(async () =>
    {
        var doc = await Plans(owner).Document(id).GetSnapshotAsync(ct);
        return doc.Exists && doc.TryGetValue<Blob>("body", out var body) ? PlanJson.Unpack(body.ByteString.ToByteArray()) : null;
    });

    public Task<IReadOnlyList<PlanSummary>> ListAsync(TravellerId owner, CancellationToken ct) => Guard(async () =>
    {
        var docs = await Plans(owner).OrderByDescending("updatedAt").Select(ListFields).GetSnapshotAsync(ct);
        return (IReadOnlyList<PlanSummary>)[.. docs.Documents.Select(d => ToSummary(d.Id, d.ToDictionary()))];
    });

    public Task SaveAsync(Plan plan, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return Guard(() => Plans(plan.Owner).Document(plan.Id).SetAsync(ToDocument(plan), SetOptions.Overwrite, ct));
    }

    public Task<bool> DeleteAsync(TravellerId owner, string id, CancellationToken ct) => Guard(async () =>
    {
        var doc = Plans(owner).Document(id);
        if (!(await doc.GetSnapshotAsync(ct)).Exists)
        {
            return false;
        }

        await doc.DeleteAsync(cancellationToken: ct);
        return true;
    });

    public Task DeleteAllAsync(TravellerId owner, CancellationToken ct) => Guard(async () =>
    {
        // At most 50 plans, well under a batch's 500 writes.
        var docs = await Plans(owner).Select(FieldPath.DocumentId).GetSnapshotAsync(ct);
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

    internal static Dictionary<string, object?> ToDocument(Plan plan)
    {
        var summary = PlanSummary.Of(plan);
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = summary.Name,
            ["status"] = summary.Status.ToString().ToLowerInvariant(),
            ["destinationName"] = summary.DestinationName,
            ["from"] = Date(summary.From),
            ["to"] = Date(summary.To),
            ["total"] = (double)summary.Total,
            ["currency"] = summary.Currency,
            ["matchPercent"] = summary.MatchPercent,
            ["imageUrl"] = summary.ImageUrl,
            ["destinationId"] = summary.DestinationId,
            ["adults"] = summary.Adults,
            ["tripBudget"] = summary.TripBudget is { } budget ? (double)budget : null,
            ["pace"] = summary.Pace?.ToString().ToLowerInvariant(),
            ["lat"] = summary.Centre?.Lat,
            ["lng"] = summary.Centre?.Lng,
            ["createdAt"] = Timestamp.FromDateTimeOffset(plan.CreatedAt),
            ["updatedAt"] = Timestamp.FromDateTimeOffset(plan.UpdatedAt),
            ["body"] = Blob.CopyFrom(PlanJson.Pack(plan)),
        };
    }

    internal static PlanSummary ToSummary(string id, IDictionary<string, object> d) => new(
        id,
        Str(d, "name") ?? "",
        Enum.TryParse<PlanStatus>(Str(d, "status"), ignoreCase: true, out var status) ? status : PlanStatus.Draft,
        Str(d, "destinationName") ?? "",
        ParseDate(Str(d, "from")),
        ParseDate(Str(d, "to")),
        d.TryGetValue("total", out var total) && total is double amount ? (decimal)amount : 0,
        Str(d, "currency") ?? "",
        d.TryGetValue("matchPercent", out var match) && match is long percent ? (int)percent : null,
        Str(d, "imageUrl"),
        d.TryGetValue("updatedAt", out var t) && t is Timestamp time ? time.ToDateTimeOffset() : DateTimeOffset.MinValue,
        Str(d, "destinationId"),
        d.TryGetValue("adults", out var adults) && adults is long a ? (int)a : null,
        d.TryGetValue("tripBudget", out var budget) && budget is double b ? (decimal)b : null,
        Enum.TryParse<Pace>(Str(d, "pace"), ignoreCase: true, out var pace) ? pace : null,
        d.TryGetValue("lat", out var lat) && lat is double la && d.TryGetValue("lng", out var lng) && lng is double ln ? new Domain.Common.GeoPoint(la, ln) : null);

    private CollectionReference Plans(TravellerId owner) => db.Collection("travellers").Document(owner.Value).Collection("plans");

    private static string? Str(IDictionary<string, object> d, string key) => d.TryGetValue(key, out var v) ? v as string : null;

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly ParseDate(string? text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : default;

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
            throw new ServiceUnavailableException("The plan store can't be reached.", ex);
        }
    }
}

/// <summary>Stands in for Firestore when it is not configured: everything works, but nothing survives a restart.</summary>
public sealed class InMemoryPlanStore : IPlanStore
{
    private readonly ConcurrentDictionary<(string Owner, string Id), byte[]> _plans = new();

    public Task<int> CountAsync(TravellerId owner, CancellationToken ct) => Task.FromResult(_plans.Keys.Count(k => string.Equals(k.Owner, owner.Value, StringComparison.Ordinal)));

    public Task AddAsync(Plan plan, CancellationToken ct) => SaveAsync(plan, ct);

    // Kept packed, as Firestore keeps them, so a plan read back is a copy and the JSON round trip is always exercised.
    public Task<Plan?> GetAsync(TravellerId owner, string id, CancellationToken ct) =>
        Task.FromResult(_plans.TryGetValue((owner.Value, id), out var packed) ? PlanJson.Unpack(packed) : null);

    public Task<IReadOnlyList<PlanSummary>> ListAsync(TravellerId owner, CancellationToken ct) => Task.FromResult<IReadOnlyList<PlanSummary>>(
        [.. _plans.Where(p => string.Equals(p.Key.Owner, owner.Value, StringComparison.Ordinal))
            .Select(p => PlanSummary.Of(PlanJson.Unpack(p.Value)))
            .OrderByDescending(s => s.UpdatedAt)
            .ThenBy(s => s.Id, StringComparer.Ordinal)]);

    public Task SaveAsync(Plan plan, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _plans[(plan.Owner.Value, plan.Id)] = PlanJson.Pack(plan);
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(TravellerId owner, string id, CancellationToken ct) => Task.FromResult(_plans.TryRemove((owner.Value, id), out _));

    public Task DeleteAllAsync(TravellerId owner, CancellationToken ct)
    {
        foreach (var key in _plans.Keys.Where(k => string.Equals(k.Owner, owner.Value, StringComparison.Ordinal)).ToList())
        {
            _plans.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }
}

/// <summary>A whole plan as gzip-compressed JSON. Domain types carry no storage attributes, so the two that need help get a converter here.</summary>
internal static class PlanJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static byte[] Pack(Plan plan)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        {
            JsonSerializer.Serialize(gzip, plan, Options);
        }

        return output.ToArray();
    }

    public static Plan Unpack(byte[] packed)
    {
        using var gzip = new GZipStream(new MemoryStream(packed), CompressionMode.Decompress);
        return JsonSerializer.Deserialize<Plan>(gzip, Options) ?? throw new JsonException("An empty plan was stored.");
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerOptions.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new DateRangeConverter());
        options.Converters.Add(new TravellerIdConverter());
        return options;
    }

    /// <summary>
    /// <see cref="DateRange"/> is made only by <see cref="DateRange.ForTrip"/>. A saved plan's dates may be in the past by now,
    /// so they are checked against their own first day: the other rules still hold.
    /// </summary>
    private sealed class DateRangeConverter : JsonConverter<DateRange>
    {
        public override DateRange Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var dates = JsonSerializer.Deserialize<StoredDates>(ref reader, options) ?? throw new JsonException("Missing dates.");
            return DateRange.ForTrip(dates.From, dates.To, dates.From);
        }

        public override void Write(Utf8JsonWriter writer, DateRange value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, new StoredDates(value.From, value.To), options);

        private sealed record StoredDates(DateOnly From, DateOnly To);
    }

    private sealed class TravellerIdConverter : JsonConverter<TravellerId>
    {
        public override TravellerId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new(reader.GetString() ?? "");

        public override void Write(Utf8JsonWriter writer, TravellerId value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Value);
    }
}
