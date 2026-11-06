using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pamplemousse.Api.Auth;
using Pamplemousse.Api.Contracts;
using Pamplemousse.Api.Mappings;
using Pamplemousse.Api.ViewModels;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Travellers;

namespace Pamplemousse.Api.Controllers;

/// <summary>The signed-in traveller's own account (spec 009). There is no route to anyone else's.</summary>
[ApiController]
[Route("api/v1/me")]
[Tags("Account")]
public sealed class MeController : ControllerBase
{
    [HttpGet]
    [EndpointName("GetProfile")]
    [EndpointSummary("The travel profile, made from the sign-in on the first call.")]
    public async Task<ActionResult<ProfileViewModel>> GetAsync(
        [FromServices] IQueryHandler<GetProfileQuery, ProfileResponse> handler,
        CancellationToken ct) =>
        Ok((await handler.HandleAsync(new GetProfileQuery(User.ToTraveller()), ct)).ToViewModel());

    [HttpPut]
    [EndpointName("UpdateProfile")]
    [EndpointSummary("Replaces the name, passport, home currency, home city and default preferences. The email can't be changed.")]
    public async Task<ActionResult<ProfileViewModel>> UpdateAsync(
        [FromBody] UpdateProfileRequest request,
        [FromServices] ICommandHandler<UpdateProfileCommand, ProfileResponse> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var profile = await handler.HandleAsync(new UpdateProfileCommand(User.ToTraveller(), request.ToChanges()), ct);
        return Ok(profile.ToViewModel());
    }

    [HttpDelete]
    [Authorize(Policy = FirebaseSignIn.RecentSignInPolicy)]
    [EndpointName("DeleteAccount")]
    [EndpointSummary("Deletes the plans, the profile and the sign-in. Needs a sign-in from the last 5 minutes.")]
    public async Task<IActionResult> DeleteAsync(
        [FromServices] ICommandHandler<DeleteAccountCommand, bool> handler,
        CancellationToken ct)
    {
        await handler.HandleAsync(new DeleteAccountCommand(User.ToTraveller()), ct);
        return NoContent();
    }
}
