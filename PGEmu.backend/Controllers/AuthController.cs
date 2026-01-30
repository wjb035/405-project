using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using PGEmuBackend.Data;
using PGEmuBackend.DTOs.Authorization;
using PGEmuBackend.Models;

namespace PGEmuBackend.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    // Dependency injection for my database context and for microsofts built in hasher
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<User> _passwordHasher;
    
    // Constructor injection
    public AuthController(
        AppDbContext db,
        IPasswordHasher<User> passwordHasher)
    {
        _db = db;
        _passwordHasher = passwordHasher;
    }

    // Login endpoint
    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        // User lookup and check for null
        var user = _db.Users
            .FirstOrDefault(u => u.Username == request.Username);
        if (user == null)
            return Unauthorized("Invalid username or password");

        // Password verification
        var result = _passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            request.Password
        );

        // If password verification fails, return invalid
        if (result != PasswordVerificationResult.Success)
            return Unauthorized("Invalid username or password");
        
        // If password verifciation succeeds, return success
        return Ok(new
        {
            message = "Login successful",
            userId = user.Id,
            username = user.Username
        });
    }
}