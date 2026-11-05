using Microsoft.AspNetCore.Mvc;
using Pamplemousse.Api.Contracts;
using Pamplemousse.Api.Mappings;
using Pamplemousse.Api.ViewModels;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Cars.Queries.GetCarPlaces;
using Pamplemousse.Application.Cars.Queries.SearchCars;
using Pamplemousse.Application.Destinations;
using Pamplemousse.Application.Destinations.Queries.GetDestination;
using Pamplemousse.Application.Destinations.Queries.GetOverview;
using Pamplemousse.Application.Destinations.Queries.ListEvents;
using Pamplemousse.Application.Destinations.Queries.ListPlaces;
using Pamplemousse.Application.Destinations.Queries.SearchDestinations;
using Pamplemousse.Application.Flights.Queries.SearchFlights;
using Pamplemousse.Application.Hotels.Queries.SearchHotels;
using Pamplemousse.Application.Ranking;

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

    [HttpGet("{id}/flights")]
    [EndpointName("SearchFlights")]
    [EndpointSummary("Recent flight prices to this city from a departure city, cheapest first or by match (Travelpayouts).")]
    public async Task<ActionResult<FlightSearchViewModel>> GetFlightsAsync(
        string id,
        [FromQuery] SearchFlightsRequest request,
        [FromServices] IQueryHandler<SearchFlightsQuery, SearchFlightsResponse> handler,
        CancellationToken ct)
    {
        var flights = await handler.HandleAsync(
            new SearchFlightsQuery(
                id, request.Origin, request.From, request.To, request.TripType, request.DirectOnly, request.Currency, request.Limit, request.Sort,
                new RankingInput(request.Budget, request.Culture, request.Food, request.Nature, request.Nightlife, request.Comfort, request.TripBudget)),
            ct);
        return Ok(flights.ToViewModel());
    }

    [HttpGet("{id}/hotels")]
    [EndpointName("SearchHotels")]
    [EndpointSummary("Hotels for the dates with the total price for the stay (Booking.com), sorted and filtered on stored data.")]
    public async Task<ActionResult<HotelSearchViewModel>> GetHotelsAsync(
        string id,
        [FromQuery] SearchHotelsRequest request,
        [FromServices] IQueryHandler<SearchHotelsQuery, SearchHotelsResponse> handler,
        CancellationToken ct)
    {
        var hotels = await handler.HandleAsync(
            new SearchHotelsQuery(
                id, request.From, request.To, request.Adults, request.Currency, request.Sort, request.MinStars, request.MinReview,
                request.MinPrice, request.MaxPrice, request.StayType, request.FreeCancellation, request.Page,
                new RankingInput(request.Budget, request.Culture, request.Food, request.Nature, request.Nightlife, request.Comfort, request.TripBudget)),
            ct);
        return Ok(hotels.ToViewModel());
    }

    [HttpGet("{id}/car-places")]
    [EndpointName("GetCarPlaces")]
    [EndpointSummary("Where a rental car can be picked up: the city centre, then nearby airports.")]
    public async Task<ActionResult<CarPlacesViewModel>> GetCarPlacesAsync(
        string id,
        [FromServices] IQueryHandler<GetCarPlacesQuery, GetCarPlacesResponse> handler,
        CancellationToken ct)
    {
        var places = await handler.HandleAsync(new GetCarPlacesQuery(id), ct);
        return Ok(places.ToViewModel());
    }

    [HttpGet("{id}/cars")]
    [EndpointName("SearchCars")]
    [EndpointSummary("Rental cars for the trip with the total price (Booking.com), sorted and filtered on one saved search.")]
    public async Task<ActionResult<CarSearchViewModel>> GetCarsAsync(
        string id,
        [FromQuery] SearchCarsRequest request,
        [FromServices] IQueryHandler<SearchCarsQuery, SearchCarsResponse> handler,
        CancellationToken ct)
    {
        var cars = await handler.HandleAsync(
            new SearchCarsQuery(
                id, request.From, request.To, request.Place, request.DriverAge, request.Currency, request.Sort, request.Size,
                request.Gearbox, request.MinSeats, request.MinBags, request.Company, request.PickUpType, request.MaxPrice,
                request.FreeCancellation, request.Page,
                new RankingInput(request.Budget, request.Culture, request.Food, request.Nature, request.Nightlife, request.Comfort, request.TripBudget)),
            ct);
        return Ok(cars.ToViewModel());
    }
}
