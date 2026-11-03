using Microsoft.AspNetCore.Mvc;
using Pamplemousse.Api.Contracts;
using Pamplemousse.Api.Mappings;
using Pamplemousse.Api.ViewModels;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Hotels.Queries.GetHotel;

namespace Pamplemousse.Api.Controllers;

[ApiController]
[Route("api/v1/hotels")]
[Tags("Hotels")]
public sealed class HotelsController : ControllerBase
{
    [HttpGet("{hotelId}")]
    [EndpointName("GetHotel")]
    [EndpointSummary("One hotel's details and its Booking.com link with the dates and travellers filled in.")]
    public async Task<ActionResult<HotelDetailsViewModel>> GetAsync(
        string hotelId,
        [FromQuery] GetHotelRequest request,
        [FromServices] IQueryHandler<GetHotelQuery, GetHotelResponse> handler,
        CancellationToken ct)
    {
        var hotel = await handler.HandleAsync(new GetHotelQuery(hotelId, request.From, request.To, request.Adults, request.Currency), ct);
        return Ok(hotel.ToViewModel());
    }
}
