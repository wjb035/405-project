using System.ComponentModel.DataAnnotations;

namespace PGEmuBackend.Models;

public class CollectionGame
{
    public int CollectionId { get; set; }

    [MaxLength(64)]
    public string ExternalGameId { get; set; } = null!;

    [Required]
    public GameSource Source { get; set; }

    // Navigation
    public UserCollection Collection { get; set; } = null!;
}