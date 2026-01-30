namespace PGEmuBackend.DTOs.Authorization;

public record LoginRequest(
    string Username,
    string Password
);