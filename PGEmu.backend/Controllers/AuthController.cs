using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using PGEmuBackend.Data;
using PGEmuBackend.Models;
using LoginRequest = PGEmuBackend.DTOs.Authorization.LoginRequest;
using PGEmuBackend.DTOs.Authorization;
using Microsoft.EntityFrameworkCore;

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
    
    // Registration endpoint
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        
        if (await _db.Users.AnyAsync(u => u.Username == request.Username))
            return BadRequest("Username already exists");

        if (await _db.Users.AnyAsync(u => u.Email == request.Email))
            return BadRequest("Email already exists");
        
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = request.Username,
            Email = request.Email,
            CreatedAt = DateTime.UtcNow
        };
        
        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);
        
        _db.Users.Add(user);
        _db.SaveChanges();

        return Ok(new
        {
            message = "User registered successfully",
            userId = user.Id,
            username = user.Username
        });
    }

    
    // Login endpoint
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        // User lookup and check for null
        var user =  await _db.Users
            .FirstOrDefaultAsync(u => u.Username == request.Username);
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