using Microsoft.AspNetCore.Mvc;

namespace Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TestController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            message = "Test endpoint working",
            timestamp = DateTime.UtcNow,
            server = "Azure App Service"
        });
    }

    [HttpGet("simple")]
    public IActionResult Simple()
    {
        return Ok("Simple test endpoint working");
    }
}
