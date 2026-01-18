using Microsoft.AspNetCore.Mvc;

namespace PGEmuBackend.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    [HttpPost("login")]
    public IActionResult Login()
    {
        return Ok("Login works");
    }
}