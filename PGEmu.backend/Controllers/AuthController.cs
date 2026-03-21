using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
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
    
    
    protected string CurrentUserId => User.FindFirst("sub")?.Value ?? "";
    protected string CurrentUsername => User.FindFirst("unique_name")?.Value ?? "";
    
    
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

        // Generate tokens immediately
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
        
        return Ok(new
        {
            accessToken,
            refreshToken = refreshTokenRaw,
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
            .FirstOrDefaultAsync(u => u.Username == request.Username || u.Email == request.Username);
        if (user == null)
            return Unauthorized("Invalid credentials");

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
            refreshToken = refreshTokenRaw,
            username = user.Username 
        });
    }
    
    
    // Forgot password endpoint
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request,
        [FromServices] EmailService emailService)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
        if (user == null)
            return Ok(new { message = "If that email is registered, a code has been sent." });
        
        // Invalidate old codes
        var oldCodes = _db.PasswordResetCodes.Where(c => c.UserId == user.Id && !c.Used);
        _db.PasswordResetCodes.RemoveRange(oldCodes);

        // Generate hashed code
        var code = Random.Shared.Next(100000, 999999).ToString();
        var codeHash = _jwtService.HashToken(code); 

        _db.PasswordResetCodes.Add(new PasswordResetCode
        {
            UserId = user.Id,
            CodeHash = codeHash,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15)
        });
        
        await _db.SaveChangesAsync();
        await emailService.SendPasswordResetCodeAsync(user.Email, code);
        
        return Ok(new { message = "If that email is registered, a code has been sent." });
    }
        
    // Reset password endpoint
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
        if (user == null)
            return BadRequest("Invalid request");
        
        // Get the reset code and check it
        var codeHash = _jwtService.HashToken(request.Code);
        var resetCode = await _db.PasswordResetCodes.FirstOrDefaultAsync(c =>
            c.UserId == user.Id &&
            c.CodeHash == codeHash &&
            !c.Used &&
            c.ExpiresAt > DateTime.UtcNow);
        
        if (resetCode == null)
            return BadRequest("Invalid or expired code");
        
        resetCode.Used = true;
        
        // Update the password
        user.PasswordHash = _passwordHasher.HashPassword(user, request.NewPassword);
        
        // Force log out every instance
        var tokens = _db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAt == null);
        await tokens.ForEachAsync(t => t.RevokedAt = DateTime.UtcNow);

        await _db.SaveChangesAsync();

        return Ok(new { message = "Password reset successfully" });
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
    
    
    // Refresh endpoint
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        // Hash the token
        var tokenHash = _jwtService.HashToken(request.RefreshToken);
        
        // Lookup in DB
        var existingToken = await _db.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash);
        
        if (existingToken == null || existingToken.RevokedAt != null || existingToken.ExpiresAt < DateTime.UtcNow)
            return Unauthorized("Invalid or expired refresh token");

        var user = existingToken.User;

        // Revoke old token
        existingToken.RevokedAt = DateTime.UtcNow;
        
        // Generate new tokens
        var newAccessToken = _jwtService.GenerateAccessToken(user);
        var newRefreshTokenRaw = _jwtService.GenerateRefreshToken();
        var newRefreshTokenHash = _jwtService.HashToken(newRefreshTokenRaw);

        var newRefreshToken = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = newRefreshTokenHash,
            ExpiresAt = DateTime.UtcNow.AddDays(double.Parse(_config["JwtSettings:RefreshTokenExpirationDays"]))
        };
        
        await _db.RefreshTokens.AddAsync(newRefreshToken);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            accessToken = newAccessToken,
            refreshToken = newRefreshTokenRaw,
        });

    }

    
    // Check if user is logged in and debug auth issue
    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
        return Ok(new
        {
            userId = CurrentUserId,
            username = CurrentUsername
        });
    }
}