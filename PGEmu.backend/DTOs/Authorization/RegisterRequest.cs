namespace PGEmuBackend.DTOs.Authorization;

public record RegisterRequest(
    string Username,
    string Email,
    string Password
);
