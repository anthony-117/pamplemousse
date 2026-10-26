using System.Reflection;

namespace Pamplemousse.Api.Features.System;

public static class SystemEndpoints
{
    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/system").WithTags("System");

        group.MapGet("/ping", (IHostEnvironment env, TimeProvider clock) => TypedResults.Ok(new PingResponse(
                Name: "pamplemousse",
                Version: typeof(SystemEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown",
                Environment: env.EnvironmentName,
                UtcNow: clock.GetUtcNow())))
            .WithName("Ping")
            .WithSummary("Confirms the API is up and reports its version.");

        return routes;
    }
}

public sealed record PingResponse(string Name, string Version, string Environment, DateTimeOffset UtcNow);
