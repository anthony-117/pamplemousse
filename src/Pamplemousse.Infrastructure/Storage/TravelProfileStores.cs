using System.Collections.Concurrent;
using Google.Cloud.Firestore;
using Grpc.Core;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Itinerary;
using Pamplemousse.Domain.Ranking;
using Pamplemousse.Domain.Travellers;

namespace Pamplemousse.Infrastructure.Storage;

/// <summary>
/// Travel profiles in Firestore, one <c>travellers/{uid}</c> document each (spec 009, data-model.md). Field names are written
/// out here so Domain types carry no storage attributes. Firestore errors become <see cref="ServiceUnavailableException"/>.
/// </summary>
public sealed class FirestoreTravelProfiles(FirestoreDb db) : ITravelProfileStore
{
    private CollectionReference Travellers => db.Collection("travellers");

    public Task<TravelProfile?> GetAsync(TravellerId traveller, CancellationToken ct) => Guard(async () =>
    {
        var doc = await Travellers.Document(traveller.Value).GetSnapshotAsync(ct);
        return doc.Exists ? ToProfile(traveller, doc.ToDictionary()) : null;
    });

    public Task SaveAsync(TravelProfile profile, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return Guard(() => Travellers.Document(profile.Traveller.Value).SetAsync(FromProfile(profile), SetOptions.Overwrite, ct));
    }

    public Task DeleteAsync(TravellerId traveller, CancellationToken ct) =>
        Guard(() => Travellers.Document(traveller.Value).DeleteAsync(cancellationToken: ct));

    internal static Dictionary<string, object?> FromProfile(TravelProfile p) => new(StringComparer.Ordinal)
    {
        ["displayName"] = p.DisplayName,
        ["email"] = p.Email,
        ["passport"] = p.Passport,
        ["homeCurrency"] = p.HomeCurrency,
        ["homeCityId"] = p.HomeCityId,
        ["preferences"] = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["budget"] = p.Preferences.Budget,
            ["culture"] = p.Preferences.Culture,
            ["food"] = p.Preferences.Food,
            ["nature"] = p.Preferences.Nature,
            ["nightlife"] = p.Preferences.Nightlife,
            ["comfort"] = p.Preferences.Comfort,
        },
        ["pace"] = p.Pace.ToText(),
        ["createdAt"] = Timestamp.FromDateTimeOffset(p.CreatedAt),
        ["updatedAt"] = Timestamp.FromDateTimeOffset(p.UpdatedAt),
    };

    internal static TravelProfile ToProfile(TravellerId traveller, IDictionary<string, object> d)
    {
        var prefs = d.TryGetValue("preferences", out var v) && v is IDictionary<string, object> m ? m : new Dictionary<string, object>(StringComparer.Ordinal);
        return new TravelProfile(
            traveller,
            Str(d, "displayName") ?? "",
            Str(d, "email") ?? "",
            Str(d, "passport") ?? TravelProfile.DefaultPassport,
            Str(d, "homeCurrency") ?? TravelProfile.DefaultCurrency,
            Str(d, "homeCityId"),
            new Weights(Weight(prefs, "budget"), Weight(prefs, "culture"), Weight(prefs, "food"), Weight(prefs, "nature"), Weight(prefs, "nightlife"), Weight(prefs, "comfort")),
            Time(d, "createdAt"),
            Time(d, "updatedAt"))
        {
            Pace = Str(d, "pace") is { } pace && Enum.TryParse<Pace>(pace, ignoreCase: true, out var known) ? known : Pace.Balanced,
        };
    }

    private static string? Str(IDictionary<string, object> d, string key) => d.TryGetValue(key, out var v) ? v as string : null;

    private static int Weight(IDictionary<string, object> d, string key) => d.TryGetValue(key, out var v) && v is long n ? (int)n : Weights.Default;

    private static DateTimeOffset Time(IDictionary<string, object> d, string key) =>
        d.TryGetValue(key, out var v) && v is Timestamp t ? t.ToDateTimeOffset() : DateTimeOffset.MinValue;

    private static async Task<T> Guard<T>(Func<Task<T>> work)
    {
        try
        {
            return await work();
        }
        catch (RpcException ex)
        {
            throw new ServiceUnavailableException("The profile store can't be reached.", ex);
        }
    }
}

/// <summary>Stands in for Firestore when it is not configured: everything works, but nothing survives a restart.</summary>
public sealed class InMemoryTravelProfiles : ITravelProfileStore
{
    private readonly ConcurrentDictionary<string, TravelProfile> _profiles = new(StringComparer.Ordinal);

    public Task<TravelProfile?> GetAsync(TravellerId traveller, CancellationToken ct) => Task.FromResult(_profiles.GetValueOrDefault(traveller.Value));

    public Task SaveAsync(TravelProfile profile, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _profiles[profile.Traveller.Value] = profile;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(TravellerId traveller, CancellationToken ct)
    {
        _profiles.TryRemove(traveller.Value, out _);
        return Task.CompletedTask;
    }
}
