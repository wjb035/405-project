using Microsoft.EntityFrameworkCore;
using Pomelo.EntityFrameworkCore.MySql;
using PGEmuBackend.Models;

namespace PGEmuBackend.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options) {}

    public DbSet<User> Users => Set<User>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();
    public DbSet<UserStatus> UserStatuses => Set<UserStatus>();
    public DbSet<UserActivity> UserActivities => Set<UserActivity>();
    public DbSet<Friend> Friends => Set<Friend>();
    public DbSet<UserCollection> UserCollections => Set<UserCollection>();
    public DbSet<CollectionGame> CollectionGames => Set<CollectionGame>();
    public DbSet<UserGame> UserGames => Set<UserGame>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Friend>()
            .HasKey(f => new { f.UserId, f.FriendId });

        modelBuilder.Entity<UserGame>()
            .HasIndex(ug => new { ug.UserId, ug.ExternalGameId, ug.Source })
            .IsUnique();

        modelBuilder.Entity<CollectionGame>()
            .HasKey(cg => new { cg.CollectionId, cg.ExternalGameId, cg.Source });
    }
}