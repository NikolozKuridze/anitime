using Microsoft.AspNetCore.Mvc;

namespace Anitime.Api.Controllers;

[Route("/")]
public sealed class SystemController : BaseApiController
{
    [HttpGet]
    public IActionResult Get() => Ok(new { name = "Anitime API", status = "ok" });
}
