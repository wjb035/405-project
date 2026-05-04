namespace PGEmuBackend.DTOs.ProfileCustomization;

public class ProfileCustomizationDTO
{
    public Guid UserId { get; set; }

    public string? Username { get; set; }

    public string? AvatarUrl { get; set; }

    public string? Bio { get; set; }

    public string? NewUsername { get; set; }
    
    public string? NewBio { get; set; }

    public string? ProfileAccent { get; set; }

    public string? AvatarFrame { get; set; }

    public string? ProfileBackground { get; set; }
    //public DateTime CreatedAt { get; }
}
