# Data Model: Weighted search

Everything here is in `Pamplemousse.Domain/Ranking/`, with no outside dependency (FR-014). Nothing new is
stored in Firestore. Redis gets two new cache entries (bottom of this page).

## Weights

```csharp
public enum Criterion { Budget, Culture, Food, Nature, Nightlife, Comfort }

public sealed record Weights(int Budget, int Culture, int Food, int Nature, int Nightlife, int Comfort)
{
    public const int Default = 50;
    // Each value 0..100; a missing one is 50. Out of range → DomainException naming the field (FR-012).
    public static Weights Create(int? budget, int? culture, int? food, int? nature, int? nightlife, int? comfort);
    public int this[Criterion c] { get; }
}
```

## Candidate: what an offer brings to the ranking

Each kind turns its offers into candidates (`HotelRanking`, `FlightRanking`, `CarRanking`). Only the criteria
that apply to that kind are present (spec: interests apply to hotels only).

```csharp
public sealed record Candidate(string Id, int SourceRank, decimal Price, IReadOnlyDictionary<Criterion, RawMeasure> Measures);

// A value compared within the list (price, nearby counts, journey time) or already on a 0..1 scale.
public abstract record RawMeasure(string Detail);
public sealed record Relative(double? Value, bool HigherIsBetter, string Detail) : RawMeasure(Detail);
public sealed record Fixed(double? Value, string Detail) : RawMeasure(Detail);  // 0..1
```

`Detail` is the plain-English fact shown in the breakdown, e.g. "12 top sights within 1.5 km",
"Reviews 9.1 / 10 · 4 stars", "Direct both ways · 3h 45m". `null` values mean unknown.

## Match: the result for one offer

```csharp
public sealed record MatchPart(Criterion Criterion, int Weight, double Value, bool Known, string Detail);

public sealed record Match(double Total, IReadOnlyList<MatchPart> Parts, string Reason, bool OverBudget)
{
    public int Percent => (int)Math.Round(Total * 100, MidpointRounding.AwayFromZero);
}

public sealed record Ranked<T>(T Offer, Match Match);
```

## ScoringEngine

```csharp
public static class ScoringEngine
{
    // Same input → same output (FR-009). No clock, no randomness, no culture-dependent formatting.
    public static IReadOnlyList<Ranked<T>> Rank<T>(
        IReadOnlyList<T> offers, Func<T, Candidate> describe, Weights weights, decimal? tripBudget, ReasonBook reasons);

    // True when every criterion this kind uses has weight 0 (FR-010).
    public static bool NothingApplies(IEnumerable<Criterion> used, Weights weights);
}
```

The steps, for one list:

1. **Normalise `Relative` measures** within the list. With known values `min` and `max`:
   `v = (x - min) / (max - min)`, reversed when lower is better. When `max == min` (all equal, or one offer),
   `v = 0.5` (spec edge cases). Unknown is 0.5, marked `Known = false`.
   - Nearby counts: a count of 0 is always 0, and the best count is 1.
   - Journey time uses `shortest / this` instead (research R-3).
2. **Fixed measures**: clamped to 0..1. Unknown is 0.5, `Known = false`.
3. **Total** = Σ(weight × v) / Σ(weight), over the criteria present. A part with weight 0 is left out of the
   breakdown.
4. **Over budget**: `tripBudget` is given and `Price > tripBudget`.
5. **Order**: not over budget first, then `Total` descending, then `SourceRank`, then `Id` (stable).
6. **Reason** from `ReasonBook` (research R-4): the two largest `weight × v` parts with `v ≥ 0.5`, joined by ", ".

## Per kind

| Kind | Budget (Relative, lower is better) | Comfort (Fixed, average of known parts) | Interests |
|---|---|---|---|
| Hotel | `Price.Total` | review / 10, stars / 5 | Culture, Food, Nature, Nightlife: Relative counts, higher is better |
| Flight | `Price.Amount` | stops (both legs), dates match; journey time as R-3 | — |
| Car | `Price.Total` | size class, company rating / 10, automatic, free cancellation | — |

Flight comfort mixes Fixed parts (stops, dates) with one list-relative part (journey time), so `FlightRanking`
resolves journey time first and passes comfort as one `Fixed` value.

## Nearby counts (Application)

```csharp
public sealed record NearbyCounts(int Food, int Nightlife, int Nature);   // 500 means "500 or more"

public interface INearbyPlaces                       // Infrastructure: OpenTripMap, two requests per position
{
    Task<NearbyCounts> CountAsync(GeoPoint position, CancellationToken ct);
}
```

`HotelNearby` (Application) resolves a list of hotels:
- counts from Redis `nearby:{hotelId}`, kept 30 days; missing ones fetched 4 at a time, each within 10 s;
- culture from the attraction list (`attractions:{destinationId}`, 1 day) and `GeoPoint.DistanceKm`.

Any failure leaves that hotel's interests unknown (0.5) and adds one warning for the whole response (FR-011).

## Changes to existing types

| Type | Change |
|---|---|
| `HotelSort`, `CarSort` | new value `Match` |
| `HotelFilters.Apply`, `CarFilters.Apply` | `Match` returns the matching offers in the source's order; ranking happens in the handler, which then pages |
| `SearchHotelsQuery`, `SearchCarsQuery`, `SearchFlightsQuery` | gain `Weights? Weights`, `decimal? TripBudget`; flights also gain `Sort` (`cheapest` default, `match`) |
| `Search…Response` | gain `IReadOnlyDictionary<string, Match>? Matches` (by offer id), `Weights? Weights`, `string? RankingNote` |

## Cache entries (Redis)

| Key | Value | Kept |
|---|---|---|
| `nearby:{hotelId}` | `NearbyCounts` | 30 days |
| `attractions:{destinationId}` | the attraction list (name, position) | 1 day |
