using Anitime.Contracts;
using Anitime.Infrastructure.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Anitime.Api.Controllers;

/// <summary>Configuration the app reads before sign-in, such as the force-update gate.</summary>
[AllowAnonymous]
[Route("api/app")]
public sealed class AppConfigController : BaseApiController
{
    [HttpGet("config")]
    [ProducesResponseType(typeof(AppConfigResponse), StatusCodes.Status200OK)]
    public IActionResult Get([FromServices] IOptionsSnapshot<ClientAppOptions> options) =>
        Ok(new AppConfigResponse(options.Value.MinIosBuild, options.Value.MinAppVersion));
}
