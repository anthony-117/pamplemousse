using Microsoft.AspNetCore.Mvc;
using Pamplemousse.Api.Auth;
using Pamplemousse.Api.Contracts;
using Pamplemousse.Api.Mappings;
using Pamplemousse.Api.ViewModels;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Itinerary;
using Pamplemousse.Domain.Itinerary;

namespace Pamplemousse.Api.Controllers;

/// <summary>The signed-in traveller's trip plans (spec 008, contracts/itinerary-api.md). Another traveller's plan is a 404.</summary>
[ApiController]
[Route("api/v1/plans")]
[Tags("Plans")]
public sealed class PlansController : ControllerBase
{
    [HttpPost]
    [EndpointName("BuildPlan")]
    [EndpointSummary("Builds a day-by-day plan from the best hotel and flight for the preferences, and saves it as a draft.")]
    [ProducesResponseType<PlanViewModel>(StatusCodes.Status201Created)]
    public async Task<ActionResult<PlanViewModel>> BuildAsync(
        [FromBody] BuildPlanRequest request,
        [FromServices] ICommandHandler<BuildPlanCommand, Plan> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var plan = await handler.HandleAsync(request.ToCommand(User.ToTraveller().Id), ct);
        return Created($"/api/v1/plans/{plan.Id}", plan.ToViewModel());
    }

    [HttpGet]
    [EndpointName("ListPlans")]
    [EndpointSummary("The traveller's plans, newest change first.")]
    public async Task<ActionResult<PlanListViewModel>> ListAsync(
        [FromServices] IQueryHandler<ListPlansQuery, PlanList> handler,
        CancellationToken ct) =>
        Ok((await handler.HandleAsync(new ListPlansQuery(User.ToTraveller().Id), ct)).ToViewModel());

    [HttpGet("{id}")]
    [EndpointName("GetPlan")]
    [EndpointSummary("A plan as saved. No source is called.")]
    public async Task<ActionResult<PlanViewModel>> GetAsync(
        string id,
        [FromServices] IQueryHandler<GetPlanQuery, Plan> handler,
        CancellationToken ct) =>
        Ok((await handler.HandleAsync(new GetPlanQuery(User.ToTraveller().Id, id), ct)).ToViewModel());

    [HttpPatch("{id}")]
    [EndpointName("ChangePlan")]
    [EndpointSummary("Renames the plan, sets it to draft or planned, or both.")]
    public async Task<ActionResult<PlanViewModel>> ChangeAsync(
        string id,
        [FromBody] ChangePlanRequest request,
        [FromServices] ICommandHandler<ChangePlanCommand, Plan> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var plan = await handler.HandleAsync(new ChangePlanCommand(User.ToTraveller().Id, id, request.Name, request.Status), ct);
        return Ok(plan.ToViewModel());
    }

    [HttpDelete("{id}")]
    [EndpointName("DeletePlan")]
    [EndpointSummary("Deletes the plan.")]
    public async Task<IActionResult> DeleteAsync(
        string id,
        [FromServices] ICommandHandler<DeletePlanCommand, bool> handler,
        CancellationToken ct)
    {
        await handler.HandleAsync(new DeletePlanCommand(User.ToTraveller().Id, id), ct);
        return NoContent();
    }

    [HttpGet("{id}/alternatives/{kind:regex(^(hotels|flights|cars)$)}")]
    [EndpointName("ListPlanAlternatives")]
    [EndpointSummary("The kept ranked hotels, flights or cars to swap from. The first call for cars runs the car search once.")]
    public async Task<IActionResult> AlternativesAsync(
        string id,
        string kind,
        [FromServices] IQueryHandler<GetAlternativesQuery, Plan> handler,
        CancellationToken ct)
    {
        var offerKind = KindOf(kind.TrimEnd('s'));
        var plan = await handler.HandleAsync(new GetAlternativesQuery(User.ToTraveller().Id, id, offerKind), ct);
        return Ok(plan.AlternativesOf(offerKind));
    }

    [HttpPut("{id}/{kind:regex(^(hotel|flight|car)$)}")]
    [EndpointName("SwapPlanOffer")]
    [EndpointSummary("Swaps in another hotel, flight or car from the kept list, and rebuilds the days, total and match.")]
    public async Task<ActionResult<PlanViewModel>> SwapAsync(
        string id,
        string kind,
        [FromBody] SwapOfferRequest request,
        [FromServices] ICommandHandler<SwapOfferCommand, Plan> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var plan = await handler.HandleAsync(new SwapOfferCommand(User.ToTraveller().Id, id, KindOf(kind), request.OfferId), ct);
        return Ok(plan.ToViewModel());
    }

    [HttpDelete("{id}/car")]
    [EndpointName("RemovePlanCar")]
    [EndpointSummary("Removes the car and rebuilds the total and match.")]
    public async Task<ActionResult<PlanViewModel>> RemoveCarAsync(
        string id,
        [FromServices] ICommandHandler<SwapOfferCommand, Plan> handler,
        CancellationToken ct)
    {
        var plan = await handler.HandleAsync(new SwapOfferCommand(User.ToTraveller().Id, id, OfferKind.Car, null, RemoveCar: true), ct);
        return Ok(plan.ToViewModel());
    }

    [HttpGet("{id}/days/{day:int}/alternatives")]
    [EndpointName("ListDayAlternatives")]
    [EndpointSummary("Up to 6 places or events that could replace an item of the day, or fill a new slot, nearest first.")]
    public async Task<ActionResult<DayAlternativesViewModel>> DayAlternativesAsync(
        string id,
        int day,
        [FromQuery] string? itemId,
        [FromQuery] string? kind,
        [FromServices] IQueryHandler<GetDayAlternativesQuery, DayAlternativesAnswer> handler,
        [FromServices] TimeProvider clock,
        CancellationToken ct)
    {
        var answer = await handler.HandleAsync(new GetDayAlternativesQuery(User.ToTraveller().Id, id, day, itemId, kind), ct);
        return Ok(answer.ToViewModel(clock.GetUtcNow()));
    }

    [HttpPost("{id}/days/{day:int}/items")]
    [EndpointName("AddOrReplaceDayItem")]
    [EndpointSummary("Adds one of the day's alternatives, or puts it in place of an item, and rebuilds the day, total and match.")]
    public async Task<ActionResult<PlanViewModel>> ChangeDayAsync(
        string id,
        int day,
        [FromBody] ChangeDayItemRequest request,
        [FromServices] ICommandHandler<ChangeDayItemCommand, Plan> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var command = new ChangeDayItemCommand(User.ToTraveller().Id, id, day, DayChange.AddOrReplace, request.Kind, request.Id, request.Replaces);
        return Ok((await handler.HandleAsync(command, ct)).ToViewModel());
    }

    [HttpDelete("{id}/days/{day:int}/items/{itemId}")]
    [EndpointName("RemoveDayItem")]
    [EndpointSummary("Removes an item of the day; its time becomes free time.")]
    public async Task<ActionResult<PlanViewModel>> RemoveDayItemAsync(
        string id,
        int day,
        string itemId,
        [FromServices] ICommandHandler<ChangeDayItemCommand, Plan> handler,
        CancellationToken ct)
    {
        var command = new ChangeDayItemCommand(User.ToTraveller().Id, id, day, DayChange.Remove, null, Uri.UnescapeDataString(itemId), null);
        return Ok((await handler.HandleAsync(command, ct)).ToViewModel());
    }

    [HttpPost("{id}/rebuild")]
    [EndpointName("RebuildPlan")]
    [EndpointSummary("Builds the plan again in place with new values, keeping its name, status, day edits and the hotel, flight and car the traveller chose. On failure the plan is unchanged.")]
    public async Task<ActionResult<PlanViewModel>> RebuildAsync(
        string id,
        [FromBody] RebuildPlanRequest request,
        [FromServices] ICommandHandler<RebuildPlanCommand, Plan> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok((await handler.HandleAsync(request.ToCommand(User.ToTraveller().Id, id), ct)).ToViewModel());
    }

    private static OfferKind KindOf(string kind) => kind switch
    {
        "hotel" => OfferKind.Hotel,
        "flight" => OfferKind.Flight,
        _ => OfferKind.Car,
    };
}
