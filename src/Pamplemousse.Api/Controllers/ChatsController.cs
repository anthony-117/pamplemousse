using Microsoft.AspNetCore.Mvc;
using Pamplemousse.Api.Auth;
using Pamplemousse.Api.Contracts;
using Pamplemousse.Api.ViewModels;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Assistant.Chats;
using Pamplemousse.Domain.Assistant;

namespace Pamplemousse.Api.Controllers;

/// <summary>
/// The signed-in traveller's chats (spec 010, contracts/chat-api.md). Another traveller's chat, or one past its 90 days,
/// is a 404. The two streamed sends are in ChatStreamController.
/// </summary>
[ApiController]
[Route("api/v1/assistant/chats")]
[Tags("Chats")]
public sealed class ChatsController : ControllerBase
{
    [HttpGet]
    [EndpointName("ListChats")]
    [EndpointSummary("Recents: the traveller's chats, latest message first.")]
    public async Task<ActionResult<ChatPageViewModel>> ListAsync(
        [FromQuery] int? limit,
        [FromQuery] string? before,
        [FromServices] IQueryHandler<ListChatsQuery, ChatList> handler,
        CancellationToken ct) =>
        Ok(ChatPageViewModel.Of(await handler.HandleAsync(new ListChatsQuery(User.ToTraveller().Id, limit, before), ct)));

    [HttpGet("search")]
    [EndpointName("SearchChats")]
    [EndpointSummary("Chats holding every word of q, then plans whose name holds it.")]
    public async Task<ActionResult<ChatSearchViewModel>> SearchAsync(
        [FromQuery] string? q,
        [FromServices] IQueryHandler<SearchChatsQuery, ChatSearch> handler,
        CancellationToken ct)
    {
        var found = await handler.HandleAsync(new SearchChatsQuery(User.ToTraveller().Id, q), ct);
        return Ok(new ChatSearchViewModel([.. found.Chats.Select(ChatListItemViewModel.Of)], [.. found.Plans.Select(ChatJson.Plan)]));
    }

    [HttpGet("{chatId}")]
    [EndpointName("GetChat")]
    [EndpointSummary("A chat with every message, its steps and cards, oldest first.")]
    public async Task<ActionResult<ChatViewModel>> GetAsync(
        string chatId,
        [FromServices] IQueryHandler<GetChatQuery, ChatWithMessages> handler,
        CancellationToken ct) =>
        Ok(ChatViewModel.Of(await handler.HandleAsync(new GetChatQuery(User.ToTraveller().Id, chatId), ct)));

    [HttpPatch("{chatId}")]
    [EndpointName("RenameChat")]
    [EndpointSummary("Renames the chat (1–40 characters).")]
    public async Task<ActionResult<ChatListItemViewModel>> RenameAsync(
        string chatId,
        [FromBody] RenameChatRequest request,
        [FromServices] ICommandHandler<RenameChatCommand, Chat> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ok(ChatListItemViewModel.Of(await handler.HandleAsync(new RenameChatCommand(User.ToTraveller().Id, chatId, request.Title), ct)));
    }

    [HttpDelete("{chatId}")]
    [EndpointName("DeleteChat")]
    [EndpointSummary("Deletes the chat and its messages. Plans built in it stay.")]
    public async Task<IActionResult> DeleteAsync(
        string chatId,
        [FromServices] ICommandHandler<DeleteChatCommand, bool> handler,
        CancellationToken ct)
    {
        await handler.HandleAsync(new DeleteChatCommand(User.ToTraveller().Id, chatId), ct);
        return NoContent();
    }
}
