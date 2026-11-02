using Microsoft.AspNetCore.Mvc;
using Pamplemousse.Api.Mappings;
using Pamplemousse.Api.ViewModels;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Destinations.Queries.GetPlace;

namespace Pamplemousse.Api.Controllers;

[ApiController]
[Route("api/v1/places")]
[Tags("Destinations")]
public sealed class PlacesController : ControllerBase
{
    [HttpGet("{id}")]
    [EndpointName("GetPlace")]
    [EndpointSummary("Place details by id, e.g. wd:Q10285 or otm:W123.")]
    public async Task<ActionResult<PlaceViewModel>> GetAsync(
        string id,
        [FromServices] IQueryHandler<GetPlaceQuery, PlaceDetailsResponse> handler,
        CancellationToken ct)
    {
        var details = await handler.HandleAsync(new GetPlaceQuery(id), ct);
        return details.Place is { } place
            ? Ok(place.ToViewModel())
            : new JsonResult(details.Warning ?? "Place not found.") { StatusCode = StatusCodes.Status404NotFound };
    }
}
