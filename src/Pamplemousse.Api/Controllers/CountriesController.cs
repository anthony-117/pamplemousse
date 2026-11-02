using Microsoft.AspNetCore.Mvc;
using Pamplemousse.Api.Mappings;
using Pamplemousse.Api.ViewModels;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Destinations.Queries.ListCountries;

namespace Pamplemousse.Api.Controllers;

[ApiController]
[Route("api/v1/countries")]
[Tags("Destinations")]
public sealed class CountriesController : ControllerBase
{
    [HttpGet]
    [EndpointName("ListCountries")]
    [EndpointSummary("Countries for passport and currency pickers.")]
    public async Task<ActionResult<IReadOnlyList<CountryOptionViewModel>>> ListAsync(
        [FromServices] IQueryHandler<ListCountriesQuery, IReadOnlyList<CountryOption>> handler,
        CancellationToken ct)
    {
        var countries = await handler.HandleAsync(new ListCountriesQuery(), ct);
        return Ok(countries.Select(c => c.ToViewModel()).ToList());
    }
}
