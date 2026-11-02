using Pamplemousse.Application.Abstractions;

namespace Pamplemousse.Application.Destinations.Queries.SearchDestinations;

/// <summary>City search for autocomplete; terms under 2 characters return nothing.</summary>
public sealed record SearchDestinationsQuery(string? Term, int? Limit);

public sealed class SearchDestinationsHandler(IReferenceData data) : IQueryHandler<SearchDestinationsQuery, IReadOnlyList<DestinationSummary>>
{
    public Task<IReadOnlyList<DestinationSummary>> HandleAsync(SearchDestinationsQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query.Term) || query.Term.Trim().Length < 2)
        {
            return Task.FromResult<IReadOnlyList<DestinationSummary>>([]);
        }

        IReadOnlyList<DestinationSummary> results = [.. data.SearchDestinations(query.Term, Math.Clamp(query.Limit ?? 10, 1, 25))
            .Select(d => DestinationSummary.From(d, data.FindCountry(d.CountryCode)))];
        return Task.FromResult(results);
    }
}
