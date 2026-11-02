using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Domain.Exceptions;

namespace Pamplemousse.Application.Destinations.Queries.GetDestination;

public sealed record GetDestinationQuery(string Id);

public sealed record DestinationDetailsResponse(DestinationSummary Destination, Country? Country);

public sealed class GetDestinationHandler(IReferenceData data) : IQueryHandler<GetDestinationQuery, DestinationDetailsResponse>
{
    public Task<DestinationDetailsResponse> HandleAsync(GetDestinationQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var destination = data.FindDestination(query.Id) ?? throw new NotFoundException($"Destination '{query.Id}' was not found.");
        var country = data.FindCountry(destination.CountryCode);
        return Task.FromResult(new DestinationDetailsResponse(DestinationSummary.From(destination, country), country));
    }
}
