using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Pamplemousse.Api.RateLimiting;

/// <summary>
/// How many assistant messages are allowed (spec 003, FR-015), from the "Assistant:RateLimit" section of appsettings.
/// Each message can make several Gemini calls on the shared free tier, so the assistant is the only limited endpoint.
/// Counts live in memory: a restart of the API starts them again.
/// </summary>
public sealed class AssistantRateLimitSettings
{
    public const string Section = "Assistant:RateLimit";

    /// <summary>Messages one client (IP address) may send per minute.</summary>
    public int PerClientPerMinute { get; set; } = 5;

    /// <summary>Messages one client may send per day.</summary>
    public int PerClientPerDay { get; set; } = 40;

    /// <summary>Messages all clients together may send per minute.</summary>
    public int AllClientsPerMinute { get; set; } = 10;
}

public static class AssistantRateLimits
{
    private const string AssistantPath = "/api/v1/assistant";

    /// <summary>
    /// Limits the assistant per client and overall, answering 429 with Retry-After. The client is the IP address,
    /// read from Caddy's X-Forwarded-For: the API's port is only reachable inside the Docker network, so private
    /// addresses are trusted as proxies.
    /// </summary>
    public static IServiceCollection AddAssistantRateLimits(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AssistantRateLimitSettings>(configuration.GetSection(AssistantRateLimitSettings.Section));

        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.ForwardLimit = 1;
            o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("10.0.0.0/8"));
            o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("172.16.0.0/12"));
            o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("192.168.0.0/16"));
        });

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                Limiter(s => s.AllClientsPerMinute, TimeSpan.FromMinutes(1), _ => "all"),
                Limiter(s => s.PerClientPerMinute, TimeSpan.FromMinutes(1), ClientKey),
                Limiter(s => s.PerClientPerDay, TimeSpan.FromDays(1), ClientKey));
            o.OnRejected = RejectAsync;
        });

        return services;
    }

    private static string ClientKey(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static PartitionedRateLimiter<HttpContext> Limiter(
        Func<AssistantRateLimitSettings, int> permits, TimeSpan window, Func<HttpContext, string> key) =>
        PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            if (!context.Request.Path.StartsWithSegments(AssistantPath, StringComparison.OrdinalIgnoreCase))
            {
                return RateLimitPartition.GetNoLimiter("unlimited");
            }

            var settings = context.RequestServices.GetRequiredService<IOptions<AssistantRateLimitSettings>>().Value;
            return RateLimitPartition.GetFixedWindowLimiter(key(context), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permits(settings),
                Window = window,
                QueueLimit = 0,
            });
        });

    private static async ValueTask RejectAsync(OnRejectedContext context, CancellationToken ct)
    {
        var response = context.HttpContext.Response;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        var problems = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problems.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context.HttpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too many messages",
                Detail = "Too many messages to the assistant. Wait a minute and try again.",
            },
        });
    }
}
