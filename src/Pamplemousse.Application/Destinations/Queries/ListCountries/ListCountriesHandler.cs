using Pamplemousse.Application.Abstractions;

namespace Pamplemousse.Application.Destinations.Queries.ListCountries;

/// <summary>Countries for passport and currency pickers.</summary>
public sealed record ListCountriesQuery;

public sealed record CountryOption(string Code, string Name, string? Flag, IReadOnlyList<string> Currencies);

public sealed class ListCountriesHandler(IReferenceData data) : IQueryHandler<ListCountriesQuery, IReadOnlyList<CountryOption>>
{
    public Task<IReadOnlyList<CountryOption>> HandleAsync(ListCountriesQuery query, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CountryOption>>([.. data.Countries.Select(c => new CountryOption(c.Code, c.Name, c.Flag, [.. c.Currencies.Select(x => x.Code)]))]);
}
