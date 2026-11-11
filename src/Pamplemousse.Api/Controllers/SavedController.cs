using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Pamplemousse.Api.Auth;
using Pamplemousse.Api.Contracts;
using Pamplemousse.Api.ViewModels;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Saved;
using Pamplemousse.Domain.Saved;

namespace Pamplemousse.Api.Controllers;

/// <summary>The signed-in traveller's saved hotels, places and events (spec 010, contracts/saved-api.md).</summary>
[ApiController]
[Route("api/v1/saved")]
[Tags("Saved")]
public sealed class SavedController : ControllerBase
{
    [HttpPut("{kind}/{id}")]
    [EndpointName("SaveItem")]
    [EndpointSummary("Saves a hotel, place or event. Saving it again changes nothing.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SaveAsync(
        string kind,
        string id,
        [FromBody] SaveItemRequest request,
        [FromServices] ICommandHandler<SaveItemCommand, SavedItem> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var item = request.Item is { ValueKind: JsonValueKind.Object } json ? json.GetRawText() : null;
        await handler.HandleAsync(
            new SaveItemCommand(User.ToTraveller().Id, kind, Uri.UnescapeDataString(id), request.DestinationId, request.Name, request.ImageUrl, request.Lat, request.Lng, item),
            ct);
        return NoContent();
    }

    [HttpDelete("{kind}/{id}")]
    [EndpointName("RemoveSavedItem")]
    [EndpointSummary("Removes a saved item; one that isn't saved is not an error.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveAsync(
        string kind,
        string id,
        [FromServices] ICommandHandler<RemoveSavedCommand, bool> handler,
        CancellationToken ct)
    {
        await handler.HandleAsync(new RemoveSavedCommand(User.ToTraveller().Id, kind, Uri.UnescapeDataString(id)), ct);
        return NoContent();
    }

    [HttpGet]
    [EndpointName("ListSaved")]
    [EndpointSummary("Saved items, newest first, optionally of one kind or one city.")]
    public async Task<ActionResult<SavedListViewModel>> ListAsync(
        [FromQuery] string? kind,
        [FromQuery] string? destinationId,
        [FromServices] IQueryHandler<ListSavedQuery, SavedList> handler,
        CancellationToken ct)
    {
        var list = await handler.HandleAsync(new ListSavedQuery(User.ToTraveller().Id, kind, destinationId), ct);
        return Ok(SavedListViewModel.Of(list));
    }

    [HttpGet("ids")]
    [EndpointName("SavedIds")]
    [EndpointSummary("The saved ids by kind, so every card can show its bookmark.")]
    public async Task<ActionResult<SavedIds>> IdsAsync(
        [FromServices] IQueryHandler<SavedIdsQuery, SavedIds> handler,
        CancellationToken ct) =>
        Ok(await handler.HandleAsync(new SavedIdsQuery(User.ToTraveller().Id), ct));
}
