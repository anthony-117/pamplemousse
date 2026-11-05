using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pamplemousse.Api.ViewModels;

namespace Pamplemousse.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/system")]
[Tags("System")]
public sealed class SystemController(IHostEnvironment env, TimeProvider clock) : ControllerBase
{
    [HttpGet("ping")]
    [EndpointName("Ping")]
    [EndpointSummary("Confirms the API is up and reports its version.")]
    public ActionResult<PingViewModel> Ping() => Ok(new PingViewModel(
        Name: "pamplemousse",
        Version: typeof(SystemController).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown",
        Environment: env.EnvironmentName,
        UtcNow: clock.GetUtcNow()));
}
