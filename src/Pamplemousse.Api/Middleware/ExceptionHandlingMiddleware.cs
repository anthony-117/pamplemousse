using Pamplemousse.Api.Auth;
using Pamplemousse.Api.Errors;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Assistant.Reply;
using Pamplemousse.Application.Itinerary;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Itinerary;
using Pamplemousse.Domain.Saved;

namespace Pamplemousse.Api.Middleware;

/// <summary>Turns domain exceptions into Problem Details (409 too-many-plans and 422 no-places carry a code), and an unreachable store into a 503. Anything else is a bug: UseExceptionHandler answers it with a 500.</summary>
public sealed class ExceptionHandlingMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (PlanLimitException ex) when (!context.Response.HasStarted)
        {
            await SignInRefusals.Refuse(context, StatusCodes.Status409Conflict, "too-many-plans", ex.Message);
        }
        catch (NoPlacesException ex) when (!context.Response.HasStarted)
        {
            await SignInRefusals.Refuse(context, StatusCodes.Status422UnprocessableEntity, "no-places", ex.Message);
        }
        catch (ChatBusyException ex) when (!context.Response.HasStarted)
        {
            await Problem(context, StatusCodes.Status409Conflict, "chat-busy", "Still answering.", ex.Message);
        }
        catch (ChatFullException ex) when (!context.Response.HasStarted)
        {
            await Problem(context, StatusCodes.Status409Conflict, "chat-full", "This chat is full.", ex.Message);
        }
        catch (SavedLimitException ex) when (!context.Response.HasStarted)
        {
            await Problem(
                context, StatusCodes.Status409Conflict, "too-many-saved", ex.Message, ex.Message,
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["max"] = SavedItem.MaxItems });
        }
        catch (DayFullException ex) when (!context.Response.HasStarted)
        {
            await Problem(context, StatusCodes.Status422UnprocessableEntity, "day-full", "This day is full.", ex.Message);
        }
        catch (DomainException ex) when (!context.Response.HasStarted)
        {
            var status = ExceptionStatusMapper.Map(ex);
            if (status == StatusCodes.Status404NotFound)
            {
                // UseStatusCodePages writes the Problem Details body, same as an unknown route.
                context.Response.StatusCode = status;
                return;
            }

            // Written as JSON whatever the Accept header: a send asks for text/event-stream, and the problem writer would
            // then drop the errors the app reads.
            var problem = new HttpValidationProblemDetails(new Dictionary<string, string[]>(StringComparer.Ordinal) { [ex.Field ?? ""] = [ex.Message] })
            {
                Status = StatusCodes.Status400BadRequest,
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
            };
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json", context.RequestAborted);
        }
        catch (ServiceUnavailableException ex) when (!context.Response.HasStarted)
        {
            await SignInRefusals.Refuse(context, StatusCodes.Status503ServiceUnavailable, "service-unavailable", ex.Message);
        }
    }

    /// <summary>Problem Details with a <c>code</c> the app acts on, the refusal's own words in <c>detail</c>, and any other fields.</summary>
    public static Task Problem(HttpContext context, int status, string code, string title, string? detail, IDictionary<string, object?>? more = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var extensions = new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = code };
        foreach (var (key, value) in more ?? new Dictionary<string, object?>(StringComparer.Ordinal))
        {
            extensions[key] = value;
        }

        return TypedResults.Problem(title: title, detail: detail, statusCode: status, extensions: extensions).ExecuteAsync(context);
    }
}
