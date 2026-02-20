using Godot;

namespace PGEmu.Services;

public partial class AuthService : Node
{
    public static AuthService Instance { get; private set; }

    public string AccessToken { get; private set; }
    public string RefreshToken { get; private set; }

    public override void _Ready()
    {
        Instance = this;
    }

    public void SetTokens(string accessToken, string refreshToken)
    {
        AccessToken = accessToken;
        RefreshToken = refreshToken;
    }

    public bool IsLoggedIn()
    {
        return !string.IsNullOrEmpty(AccessToken);
    }
}