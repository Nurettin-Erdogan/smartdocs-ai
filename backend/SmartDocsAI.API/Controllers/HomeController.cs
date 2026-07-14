using Microsoft.AspNetCore.Mvc;

namespace SmartDocsAI.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class HomeController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Get() => Ok(new
    {
        Service = "SmartDocs AI API",
        Status = "ok",
        Timestamp = DateTimeOffset.UtcNow
    });
}
