using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Travellers;
using Pamplemousse.Domain.Travellers;

namespace Pamplemousse.Api.Auth;

/// <summary>The Firebase project whose sign-ins are accepted. Empty: every protected route answers 503 (research R-6).</summary>
public sealed class SignInSettings
{
    public string ProjectId { get; set; } = "";

    public bool IsConfigured => ProjectId.Length > 0;
}

/// <summary>
/// Every route needs a Firebase sign-in with a confirmed email unless marked <c>[AllowAnonymous]</c> (spec 009, FR-007,
/// ADR 0019). The JWT bearer handler checks the token locally with Google's published keys.
/// </summary>
public static class FirebaseSignIn
{
    /// <summary>For deleting the account: a sign-in from the last <see cref="RecentWindow"/> (research R-3).</summary>
    public const string RecentSignInPolicy = "RecentSignIn";

    public static readonly TimeSpan RecentWindow = TimeSpan.FromMinutes(5);

    public static string Issuer(string projectId) => $"https://securetoken.google.com/{projectId}";

    public static IServiceCollection AddFirebaseSignIn(this IServiceCollection services, string projectId)
    {
        services.Configure<SignInSettings>(o => o.ProjectId = projectId);
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.MapInboundClaims = false;
            o.Authority = projectId.Length > 0 ? Issuer(projectId) : null;
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = Issuer(projectId),
                ValidAudience = projectId,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                RequireSignedTokens = true,
                RequireExpirationTime = true,
                ClockSkew = TimeSpan.FromMinutes(1),
                NameClaimType = "name",
            };
            o.Events = new JwtBearerEvents { OnTokenValidated = CheckTravellerAsync };
        });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(SignedIn().Build())
            .AddPolicy(RecentSignInPolicy, SignedIn().AddRequirements(new RecentSignInRequirement()).Build());
        services.AddSingleton<IAuthorizationHandler, ConfirmedEmailHandler>();
        services.AddSingleton<IAuthorizationHandler, RecentSignInHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, SignInRefusals>();
        return services;
    }

    private static AuthorizationPolicyBuilder SignedIn() => new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .AddRequirements(new ConfirmedEmailRequirement());

    /// <summary>The traveller the token names, for handlers (FR-008).</summary>
    public static SignedInTraveller ToTraveller(this ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new SignedInTraveller(new TravellerId(user.FindFirstValue("sub") ?? ""), user.FindFirstValue("email"), user.FindFirstValue("name"));
    }

    /// <summary>A token needs a user id, and one of an account deleted in the last hour is refused (research R-5).</summary>
    private static async Task CheckTravellerAsync(TokenValidatedContext context)
    {
        var sub = context.Principal?.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(sub) || sub.Length > TravellerId.MaxLength)
        {
            context.Fail("The token names no traveller.");
            return;
        }

        var deleted = context.HttpContext.RequestServices.GetRequiredService<IDeletedTravellers>();
        if (await deleted.ContainsAsync(new TravellerId(sub), context.HttpContext.RequestAborted))
        {
            context.Fail("The account was deleted.");
        }
    }
}

/// <summary>The sign-in's email is confirmed (FR-004). Google accounts always are.</summary>
public sealed class ConfirmedEmailRequirement : IAuthorizationRequirement;

/// <summary>The traveller signed in within <see cref="FirebaseSignIn.RecentWindow"/>.</summary>
public sealed class RecentSignInRequirement : IAuthorizationRequirement;

public sealed class ConfirmedEmailHandler : AuthorizationHandler<ConfirmedEmailRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ConfirmedEmailRequirement requirement)
    {
        if (string.Equals(context.User.FindFirstValue("email_verified"), "true", StringComparison.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

public sealed class RecentSignInHandler(TimeProvider clock) : AuthorizationHandler<RecentSignInRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, RecentSignInRequirement requirement)
    {
        if (long.TryParse(context.User.FindFirstValue("auth_time"), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            && clock.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(seconds) <= FirebaseSignIn.RecentWindow)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Turns a refused request into Problem Details with a <c>code</c> the app acts on (contracts/accounts-api.md). The title
/// never says which check failed beyond "sign in again" or "confirm your email" (FR-007).
/// </summary>
public sealed class SignInRefusals(IOptions<SignInSettings> settings) : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorizeResult);
        // Only protected routes get here ([AllowAnonymous] ones skip it); without a project none is let through.
        if (!settings.Value.IsConfigured)
        {
            return Refuse(context, StatusCodes.Status503ServiceUnavailable, "sign-in-not-configured", "Sign-in is not configured: set FIREBASE_PROJECT_ID.");
        }

        if (authorizeResult.Succeeded || !(authorizeResult.Challenged || authorizeResult.Forbidden))
        {
            return _default.HandleAsync(next, context, policy, authorizeResult);
        }

        var failed = authorizeResult.AuthorizationFailure?.FailedRequirements.ToList() ?? [];
        return authorizeResult.Forbidden switch
        {
            true when failed.OfType<ConfirmedEmailRequirement>().Any() =>
                Refuse(context, StatusCodes.Status403Forbidden, "email-not-confirmed", "Confirm your email."),
            true when failed.OfType<RecentSignInRequirement>().Any() =>
                Refuse(context, StatusCodes.Status401Unauthorized, "recent-sign-in-required", "Sign in again to continue."),
            _ => Refuse(context, StatusCodes.Status401Unauthorized, "sign-in-required", "Sign in again."),
        };
    }

    public static Task Refuse(HttpContext context, int status, string code, string title)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (status == StatusCodes.Status401Unauthorized)
        {
            context.Response.Headers.WWWAuthenticate = "Bearer";
        }

        return TypedResults.Problem(title: title, statusCode: status, extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = code })
            .ExecuteAsync(context);
    }
}
