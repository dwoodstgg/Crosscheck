using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Waypoint.Web.Controllers.Api.V1;

[ApiController]
[Route("api/v1/meta")]
public class MetaController : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Get() => Ok(new
    {
        name = "Waypoint",
        apiVersion = "v1",
    });
}
