namespace PGEmuBackend.DTOs.Authorization;

public record ResetPasswordRequest(string Email, string Code, string NewPassword);