using Pamplemousse.Api.Auth;
using Pamplemousse.Api.Errors;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Itinerary;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Itinerary;

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
        catch (DomainException ex) when (!context.Response.HasStarted)
        {
            var status = ExceptionStatusMapper.Map(ex);
            if (status == StatusCodes.Status404NotFound)
            {
                // UseStatusCodePages writes the Problem Details body, same as an unknown route.
                context.Response.StatusCode = status;
                return;
            }

            var errors = new Dictionary<string, string[]>(StringComparer.Ordinal) { [ex.Field ?? ""] = [ex.Message] };
            await TypedResults.ValidationProblem(errors).ExecuteAsync(context);
        }
        catch (ServiceUnavailableException ex) when (!context.Response.HasStarted)
        {
            await SignInRefusals.Refuse(context, StatusCodes.Status503ServiceUnavailable, "service-unavailable", ex.Message);
        }
    }
}
