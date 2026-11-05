using Microsoft.AspNetCore.Mvc;
using Pamplemousse.Api.Contracts;
using Pamplemousse.Api.Mappings;
using Pamplemousse.Api.ViewModels;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Assistant.Queries.AskAssistant;

namespace Pamplemousse.Api.Controllers;

[ApiController]
[Route("api/v1/assistant")]
[Tags("Assistant")]
public sealed class AssistantController : ControllerBase
{
    [HttpPost("messages")]
    [EndpointName("AskAssistant")]
    [EndpointSummary("Answers the traveller's last message with TravelGenie's data (Gemini). Send the recent conversation each time.")]
    public async Task<ActionResult<AssistantReplyViewModel>> AskAsync(
        [FromBody] AskAssistantRequest request,
        [FromServices] IQueryHandler<AskAssistantQuery, AskAssistantResponse> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var reply = await handler.HandleAsync(request.ToQuery(), ct);
        return Ok(reply.ToViewModel());
    }
}
