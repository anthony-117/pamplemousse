using Microsoft.AspNetCore.Mvc;
using Pamplemousse.Api.Contracts;
using Pamplemousse.Api.Mappings;
using Pamplemousse.Api.ViewModels;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Destinations;
using Pamplemousse.Application.Destinations.Queries.GetDestination;
using Pamplemousse.Application.Destinations.Queries.GetOverview;
using Pamplemousse.Application.Destinations.Queries.ListEvents;
using Pamplemousse.Application.Destinations.Queries.ListPlaces;
using Pamplemousse.Application.Destinations.Queries.SearchDestinations;

namespace Pamplemousse.Api.Controllers;

[ApiController]
[Route("api/v1/destinations")]
[Tags("Destinations")]
public sealed class DestinationsController : ControllerBase
{
    [HttpGet]
    [EndpointName("SearchDestinations")]
    [EndpointSummary("City search for autocomplete (at least 2 characters).")]
    public async Task<ActionResult<IReadOnlyList<DestinationSummaryViewModel>>> SearchAsync(
        [FromQuery] SearchDestinationsRequest request,
        [FromServices] IQueryHandler<SearchDestinationsQuery, IReadOnlyList<DestinationSummary>> handler,
        CancellationToken ct)
    {
        var results = await handler.HandleAsync(new SearchDestinationsQuery(request.Q, request.Limit), ct);
        return Ok(results.Select(r => r.ToViewModel()).ToList());
    }

    [HttpGet("{id}")]
    [EndpointName("GetDestination")]
    public async Task<ActionResult<DestinationDetailsViewModel>> GetAsync(
        string id,
        [FromServices] IQueryHandler<GetDestinationQuery, DestinationDetailsResponse> handler,
        CancellationToken ct)
    {
        var details = await handler.HandleAsync(new GetDestinationQuery(id), ct);
        return Ok(details.ToViewModel());
    }

    [HttpGet("{id}/overview")]
    [EndpointName("GetDestinationOverview")]
    [EndpointSummary("Country facts, weather, exchange rate, holidays, visa and safety for the trip dates.")]
    public async Task<ActionResult<OverviewViewModel>> GetOverviewAsync(
        string id,
        [FromQuery] GetOverviewRequest request,
        [FromServices] IQueryHandler<GetOverviewQuery, OverviewResponse> handler,
        CancellationToken ct)
    {
        var overview = await handler.HandleAsync(new GetOverviewQuery(id, request.Passport, request.Currency, request.From, request.To), ct);
        return Ok(overview.ToViewModel());
    }

    [HttpGet("{id}/places")]
    [EndpointName("GetDestinationPlaces")]
    [EndpointSummary("Top attractions (Wikidata, then OpenTripMap) or restaurants (OpenTripMap).")]
    public async Task<ActionResult<PlacesViewModel>> GetPlacesAsync(
        string id,
        [FromQuery] ListPlacesRequest request,
        [FromServices] IQueryHandler<ListPlacesQuery, PlacesResponse> handler,
        CancellationToken ct)
    {
        var places = await handler.HandleAsync(new ListPlacesQuery(id, request.Category, request.Limit), ct);
        return Ok(places.ToViewModel());
    }

    [HttpGet("{id}/events")]
    [EndpointName("GetDestinationEvents")]
    [EndpointSummary("Events near the city during the trip dates (Ticketmaster).")]
    public async Task<ActionResult<EventsViewModel>> GetEventsAsync(
        string id,
        [FromQuery] ListEventsRequest request,
        [FromServices] IQueryHandler<ListEventsQuery, EventsResponse> handler,
        CancellationToken ct)
    {
        var events = await handler.HandleAsync(new ListEventsQuery(id, request.From, request.To, request.Limit), ct);
        return Ok(events.ToViewModel());
    }
}
