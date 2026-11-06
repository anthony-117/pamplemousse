using Pamplemousse.Api.Auth;
using Pamplemousse.Api.Errors;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Exceptions;

namespace Pamplemousse.Api.Middleware;

/// <summary>Turns domain exceptions into Problem Details, and an unreachable store into a 503. Anything else is a bug: UseExceptionHandler answers it with a 500.</summary>
public sealed class ExceptionHandlingMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
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
