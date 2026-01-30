using System.ComponentModel.DataAnnotations;

namespace PGEmuBackend.Models;

public class UserCollection
{
    [Key]
    public int Id { get; set; }

    public Guid UserId { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public User User { get; set; } = null!;
    public ICollection<CollectionGame> CollectionGames { get; set; } = new List<CollectionGame>();
}