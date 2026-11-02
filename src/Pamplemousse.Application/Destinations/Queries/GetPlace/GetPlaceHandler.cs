using Microsoft.Extensions.Logging;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Common;
using Pamplemousse.Domain.Places;

namespace Pamplemousse.Application.Destinations.Queries.GetPlace;

/// <summary>A place by its "{source}:{providerId}" id, e.g. "wd:Q10285".</summary>
public sealed record GetPlaceQuery(string Id);

/// <summary><see cref="Place"/> is null when it does not exist or its source failed; <see cref="Warning"/> says which source failed.</summary>
public sealed record PlaceDetailsResponse(Place? Place, string? Warning);

public sealed class GetPlaceHandler(IEnumerable<IPlaceProvider> providers, ILogger<GetPlaceHandler> logger) : IQueryHandler<GetPlaceQuery, PlaceDetailsResponse>
{
    public async Task<PlaceDetailsResponse> HandleAsync(GetPlaceQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var separator = query.Id.IndexOf(':', StringComparison.Ordinal);
        var provider = separator > 0 ? providers.FirstOrDefault(p => string.Equals(p.Source, query.Id[..separator], StringComparison.Ordinal)) : null;
        if (provider is null)
        {
            return new PlaceDetailsResponse(null, null);
        }

        var sections = new SectionLoader(logger);
        var place = await sections.LoadAsync("Place details", () => provider.GetAsync(query.Id[(separator + 1)..], ct));
        return new PlaceDetailsResponse(place, sections.Warnings is [var warning, ..] ? warning : null);
    }
}
