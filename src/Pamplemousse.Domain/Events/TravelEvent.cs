using Pamplemousse.Domain.Common;

namespace Pamplemousse.Domain.Events;

public sealed record TravelEvent(
    string Id,
    string Name,
    DateOnly Date,
    string? Time,
    string? Venue,
    string? Category,
    Money? PriceFrom,
    string? ImageUrl,
    string Url,
    string Source);
