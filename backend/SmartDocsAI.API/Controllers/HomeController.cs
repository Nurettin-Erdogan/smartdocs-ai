using Microsoft.AspNetCore.Mvc;

namespace SmartDocsAI.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HomeController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get()
        {
            return Ok("SmartDocs AI Backend Çalışıyor!");
        }
    }
}