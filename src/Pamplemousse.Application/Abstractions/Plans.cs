using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Itinerary;
using Pamplemousse.Domain.Travellers;

namespace Pamplemousse.Application.Abstractions;

/// <summary>
/// A traveller's saved plans (spec 008, FR-012 to FR-015). Every call names the owner, so a plan of another traveller is
/// never found. The store is unreachable → <see cref="ServiceUnavailableException"/>.
/// </summary>
public interface IPlanStore
{
    Task<int> CountAsync(TravellerId owner, CancellationToken ct);

    Task AddAsync(Plan plan, CancellationToken ct);

    Task<Plan?> GetAsync(TravellerId owner, string id, CancellationToken ct);

    /// <summary>Newest change first.</summary>
    Task<IReadOnlyList<PlanSummary>> ListAsync(TravellerId owner, CancellationToken ct);

    Task SaveAsync(Plan plan, CancellationToken ct);

    /// <summary>False when there was no such plan.</summary>
    Task<bool> DeleteAsync(TravellerId owner, string id, CancellationToken ct);

    Task DeleteAllAsync(TravellerId owner, CancellationToken ct);
}

/// <summary>
/// A line of the "My plans" list, read without the whole plan. The city id, adults, budget, pace and centre are null on
/// plans saved before spec 010 until they are next saved.
/// </summary>
public sealed record PlanSummary(
    string Id,
    string Name,
    PlanStatus Status,
    string DestinationName,
    DateOnly From,
    DateOnly To,
    decimal Total,
    string Currency,
    int? MatchPercent,
    string? ImageUrl,
    DateTimeOffset UpdatedAt,
    string? DestinationId = null,
    int? Adults = null,
    decimal? TripBudget = null,
    Pace? Pace = null,
    GeoPoint? Centre = null)
{
    public static PlanSummary Of(Plan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new PlanSummary(
            plan.Id,
            plan.Name,
            plan.Status,
            plan.Request.Destination.Name,
            plan.Request.Dates.From,
            plan.Request.Dates.To,
            plan.Built.Total.Amount,
            plan.Built.Total.Currency,
            plan.Built.Match?.Percent,
            ImageOf(plan),
            plan.UpdatedAt,
            plan.Request.Destination.Id,
            plan.Request.Adults,
            plan.Request.TripBudget,
            plan.Request.Pace,
            plan.Request.Destination.Centre);
    }

    /// <summary>The chosen hotel's first photo, or else the first place with a picture.</summary>
    private static string? ImageOf(Plan plan)
    {
        var hotel = plan.Offers.Hotel?.Offer.Hotel.PhotoUrls;
        if (hotel is { Count: > 0 })
        {
            return hotel[0];
        }

        return plan.Built.Days.SelectMany(d => d.Items).Select(i => i.Ref?.ImageUrl).FirstOrDefault(u => u is not null);
    }
}
