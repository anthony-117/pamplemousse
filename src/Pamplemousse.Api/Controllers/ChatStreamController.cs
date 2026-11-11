using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Pamplemousse.Api.Auth;
using Pamplemousse.Api.Contracts;
using Pamplemousse.Api.Mappings;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Assistant.Reply;
using Pamplemousse.Domain.Assistant;

namespace Pamplemousse.Api.Controllers;

/// <summary>
/// The two sends of spec 010 (FR-001, contracts/chat-api.md): a refusal is Problem Details before any byte; otherwise
/// Server-Sent Events <c>chat</c>, <c>step</c>…, then <c>final</c> (or <c>error</c>), with <c>: ping</c> every 10 s
/// (research R-1). If the app goes away the answer still finishes and is saved (research R-3).
/// </summary>
[ApiController]
[Route("api/v1/assistant/chats")]
[Tags("Chats")]
public sealed class ChatStreamController : ControllerBase
{
    /// <summary>A quiet stream gets a comment this often, so proxies and the app's watchdog keep it open.</summary>
    public static readonly TimeSpan PingEvery = TimeSpan.FromSeconds(10);

    [HttpPost]
    [EndpointName("StartChat")]
    [EndpointSummary("Starts a chat with its first message; the answer streams as Server-Sent Events.")]
    [Produces("text/event-stream")]
    public Task StartAsync(
        [FromBody] SendMessageRequest request,
        [FromServices] ICommandHandler<SendMessageCommand, ReplyAnswer> handler,
        CancellationToken ct) => SendAsync(null, request, handler, ct);

    [HttpPost("{chatId}/messages")]
    [EndpointName("SendChatMessage")]
    [EndpointSummary("Sends the next message of a chat; the answer streams as Server-Sent Events.")]
    [Produces("text/event-stream")]
    public Task ContinueAsync(
        string chatId,
        [FromBody] SendMessageRequest request,
        [FromServices] ICommandHandler<SendMessageCommand, ReplyAnswer> handler,
        CancellationToken ct) => SendAsync(chatId, request, handler, ct);

    private async Task SendAsync(string? chatId, SendMessageRequest request, ICommandHandler<SendMessageCommand, ReplyAnswer> handler, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var t = request.Trip;
        var p = request.Preferences;
        var a = request.About;
        var command = new SendMessageCommand(
            User.ToTraveller().Id,
            chatId,
            request.Text,
            t is null ? null : new TripInput(t.DestinationId, t.From, t.To, t.Adults, t.OriginId, t.Passport, t.Currency, t.TripBudget),
            p is null ? null : new WeightsInput(p.Budget, p.Culture, p.Food, p.Nature, p.Nightlife, p.Comfort),
            request.Pace,
            a is null ? null : new AboutInput(a.PlanId, a.Day, a.ItemId));

        // Refusals throw here, before the response starts, and become Problem Details.
        var answer = await handler.HandleAsync(command, ct);

        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        try
        {
            await WriteEventsAsync(answer.Events, ct);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException)
        {
            // The app went away; the answer goes on and is saved.
        }
    }

    private async Task WriteEventsAsync(ChannelReader<ReplyEvent> events, CancellationToken ct)
    {
        Task<bool>? waiting = null;
        while (true)
        {
            waiting ??= events.WaitToReadAsync(ct).AsTask();
            var first = await Task.WhenAny(waiting, Task.Delay(PingEvery, ct));
            if (first != waiting)
            {
                await WriteAsync(": ping\n\n", ct);
                continue;
            }

            var more = await waiting;
            waiting = null;
            if (!more)
            {
                return;
            }

            while (events.TryRead(out var e))
            {
                await WriteAsync(Format(e), ct);
                if (e is ReplyFinal or ReplyFailed)
                {
                    return;
                }
            }
        }
    }

    private async Task WriteAsync(string text, CancellationToken ct)
    {
        await Response.Body.WriteAsync(Encoding.UTF8.GetBytes(text), ct);
        await Response.Body.FlushAsync(ct);
    }

    internal static string Format(ReplyEvent e)
    {
        var (name, data) = e switch
        {
            ReplyStarted s => ("chat", new JsonObject { ["chatId"] = s.ChatId, ["title"] = s.Title, ["messageId"] = s.MessageId }),
            ReplyStep s => ("step", Step(s.Step)),
            ReplyFinal f => ("final", Final(f)),
            ReplyFailed f => ("error", new JsonObject { ["warning"] = f.Warning }),
            _ => ("error", new JsonObject { ["warning"] = "The connection was lost. Try again." }),
        };
        return $"event: {name}\ndata: {data.ToJsonString(CardJson.Options)}\n\n";
    }

    private static JsonObject Step(ToolStep s) => new()
    {
        ["tool"] = s.Tool,
        ["summary"] = s.Summary,
        ["ok"] = s.Ok,
        ["total"] = s.Total,
        ["cards"] = Cards(s.Cards),
    };

    private static JsonObject Final(ReplyFinal f)
    {
        var m = f.Message;
        var data = new JsonObject
        {
            ["messageId"] = m.Id,
            ["text"] = m.Text,
            ["cards"] = Cards(m.Cards),
            ["suggestions"] = new JsonArray([.. m.Suggestions.Select(x => (JsonNode)x)]),
            ["warnings"] = new JsonArray([.. m.Warnings.Select(x => (JsonNode)x)]),
            ["title"] = f.Title,
            ["at"] = m.At,
        };
        if (m.PlanChanged is { } plan)
        {
            data["planChanged"] = plan;
        }

        return data;
    }

    private static JsonArray Cards(IReadOnlyList<string> cards) => new([.. cards.Select(c => JsonNode.Parse(c))]);
}
