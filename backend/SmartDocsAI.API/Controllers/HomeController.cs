using Microsoft.AspNetCore.Mvc;

namespace SmartDocsAI.API.Controllers
{
    // Backend'in çalışıp çalışmadığını kontrol eden basit endpoint.
    [ApiController]
    [Route("api/[controller]")]
    public class HomeController : ControllerBase
    {
        // GET /api/home isteğine 200 OK cevabı döndürür.
        [HttpGet]
        public IActionResult Get()
        {
            return Ok("SmartDocs AI Backend Çalışıyor!");
        }
    }
}
