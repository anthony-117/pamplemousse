using Microsoft.Extensions.Logging;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Cars;
using Pamplemousse.Domain.Exceptions;

namespace Pamplemousse.Application.Cars.Queries.GetCarPlaces;

public sealed record GetCarPlacesQuery(string DestinationId);

public sealed record GetCarPlacesResponse(string DestinationId, IReadOnlyList<PickUpPlace> Places, IReadOnlyList<string> Warnings);

/// <summary>Where a car can be picked up in a destination (spec 004, FR-002): the centre, then nearby airports.</summary>
public sealed class GetCarPlacesHandler(
    IReferenceData data,
    ICarProvider provider,
    ICarAirportStore airports,
    ICarHistory history,
    CarSettings settings,
    TimeProvider clock,
    ILogger<GetCarPlacesHandler> logger) : IQueryHandler<GetCarPlacesQuery, GetCarPlacesResponse>
{
    public async Task<GetCarPlacesResponse> HandleAsync(GetCarPlacesQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var destination = data.FindDestination(query.DestinationId) ?? throw new NotFoundException($"Destination '{query.DestinationId}' was not found.");
        var store = new CarData(airports, history, logger);
        var places = new CarPlaces(provider, store, settings, clock, logger);
        var list = await places.ListAsync(destination, ct);
        return new GetCarPlacesResponse(destination.Id, list, [.. places.Warnings, .. store.Warnings]);
    }
}
