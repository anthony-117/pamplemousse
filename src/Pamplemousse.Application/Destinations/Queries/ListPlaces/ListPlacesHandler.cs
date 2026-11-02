using Microsoft.Extensions.Logging;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Common;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Places;

namespace Pamplemousse.Application.Destinations.Queries.ListPlaces;

/// <summary><paramref name="Category"/> is "attraction" (the default) or "restaurant", in any case.</summary>
public sealed record ListPlacesQuery(string DestinationId, string? Category, int? Limit);

public sealed record PlacesResponse(PlaceCategory Category, IReadOnlyList<Place> Items, IReadOnlyList<string> Warnings);

public sealed class ListPlacesHandler(IReferenceData data, IEnumerable<IPlaceProvider> providers, ILogger<ListPlacesHandler> logger)
    : IQueryHandler<ListPlacesQuery, PlacesResponse>
{
    public async Task<PlacesResponse> HandleAsync(ListPlacesQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var destination = data.FindDestination(query.DestinationId) ?? throw new NotFoundException($"Destination '{query.DestinationId}' was not found.");

        var category = PlaceCategory.Attraction;
        if (query.Category is not null && (!Enum.TryParse(query.Category, ignoreCase: true, out category) || !Enum.IsDefined(category)))
        {
            throw new DomainException("Use 'attraction' or 'restaurant'.", "category");
        }

        var sections = new SectionLoader(logger);
        var provider = providers.First(p => p.Category == category);
        var items = await sections.LoadAsync(
            category == PlaceCategory.Attraction ? "Attractions" : "Restaurants",
            () => provider.SearchAsync(destination, Math.Clamp(query.Limit ?? 20, 1, 20), ct));
        return new PlacesResponse(category, items ?? [], sections.Warnings);
    }
}
