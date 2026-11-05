namespace Pamplemousse.Api.Contracts;

/// <summary>Query string of GET /api/v1/destinations/{id}/cars. <paramref name="Size"/>, <paramref name="Company"/> and <paramref name="PickUpType"/> can repeat.</summary>
public sealed record SearchCarsRequest(
    DateOnly? From,
    DateOnly? To,
    string? Place,
    int? DriverAge,
    string? Currency,
    string? Sort,
    IReadOnlyList<string>? Size,
    string? Gearbox,
    int? MinSeats,
    int? MinBags,
    IReadOnlyList<string>? Company,
    IReadOnlyList<string>? PickUpType,
    decimal? MaxPrice,
    bool? FreeCancellation,
    int? Page);
