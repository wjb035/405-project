using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using PGEmuBackend.Data;
using PGEmuBackend.Models;
using LoginRequest = PGEmuBackend.DTOs.Authorization.LoginRequest;
using PGEmuBackend.DTOs.Authorization;
using Microsoft.EntityFrameworkCore;
using PGEmuBackend.Services;


namespace PGEmuBackend.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    // Dependency injection for my database context and for microsofts built in hasher
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly JwtService _jwtService;
    private readonly IConfiguration _config;
    
    // Constructor injection
    public AuthController(
        AppDbContext db,
        IPasswordHasher<User> passwordHasher,
        JwtService jwtService,
        IConfiguration config)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _jwtService = jwtService;
        _config = config;
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
        
        await _db.Users.AddAsync(user);
        await _db.SaveChangesAsync();

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
        
        if (result != PasswordVerificationResult.Success)
            return Unauthorized("Invalid username or password");
        
        // Generate tokens
        var accessToken = _jwtService.GenerateAccessToken(user);
        var refreshTokenRaw = _jwtService.GenerateRefreshToken();
        var refreshTokenHash = _jwtService.HashToken(refreshTokenRaw);
        
        var refreshToken = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refreshTokenHash,
            ExpiresAt = DateTime.UtcNow.AddDays(double.Parse(_config["JwtSettings:RefreshTokenExpirationDays"]))
        };
        
        await _db.RefreshTokens.AddAsync(refreshToken);
        await _db.SaveChangesAsync();
        
        // If password verifciation succeeds, return access token
        return Ok(new
        {
            accessToken,
            refreshToken = refreshTokenRaw
        });
    }
    
    // Logout endpoint
    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest request)
    {
        var hash = _jwtService.HashToken(request.RefreshToken);
        var token = await _db.RefreshTokens.FirstOrDefaultAsync(rt => rt.TokenHash == hash);
        if (token != null)
        {
            token.RevokedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        return Ok(new { message = "Logged out successfully" });
    }
}