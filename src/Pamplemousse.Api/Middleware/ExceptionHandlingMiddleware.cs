using Pamplemousse.Api.Errors;
using Pamplemousse.Domain.Exceptions;

namespace Pamplemousse.Api.Middleware;

/// <summary>Turns domain exceptions into Problem Details. Anything else is a bug: UseExceptionHandler answers it with a 500.</summary>
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
    }
}
