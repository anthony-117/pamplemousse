namespace Pamplemousse.Domain.Hotels;

/// <summary>One place to stay for a search. <see cref="Rank"/> is the source's recommended order, from 1.</summary>
public sealed record HotelOffer(Hotel Hotel, HotelPrice Price, int Rank, double DistanceKm, bool NearCity);

/// <summary>One saved call to the source (ADR 0017): what was asked, when, and each hotel's price. Never changed afterwards.</summary>
public sealed record PriceSnapshot(
    string SearchKey,
    string DestinationId,
    DateOnly CheckIn,
    DateOnly CheckOut,
    int Adults,
    string Currency,
    DateTimeOffset FetchedAt,
    int Page,
    IReadOnlyList<SnapshotPrice> Prices);

public sealed record SnapshotPrice(string HotelId, decimal Total, decimal PerNight, int Rank, bool? FreeCancellation);

/// <summary>A TravelGenie destination and the source's ids for it. <see cref="SourceName"/> is the city the source matched.</summary>
public sealed record CityMapping(string DestinationId, string LocationId, long? Ufi, string SourceName, DateTimeOffset MatchedAt);
