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
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    
    // Fluent API for database migration
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Pomelo hooks provider specific behavior
        base.OnModelCreating(modelBuilder);
        
        // Users
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Username)
            .IsUnique();

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();
        
        
        // User Profiles
        modelBuilder.Entity<UserProfile>()
            .HasKey(p => p.UserId);
        
        modelBuilder.Entity<UserProfile>()
            .HasOne(p => p.User)
            .WithOne(u => u.Profile)
            .HasForeignKey<UserProfile>(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        
        
        // Friends
        modelBuilder.Entity<Friend>()
            .HasKey(f => new { f.UserId, f.FriendId });
        
        modelBuilder.Entity<Friend>()
            .HasOne(f => f.User)
            .WithMany()
            .HasForeignKey(f => f.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Friend>()
            .HasOne(f => f.FriendUser)
            .WithMany()
            .HasForeignKey(f => f.FriendId)
            .OnDelete(DeleteBehavior.Restrict);
        
        modelBuilder.Entity<Friend>(entity =>
        {
            entity.ToTable(tb =>
            {
                tb.HasCheckConstraint("chk_not_self_friend", "UserId <> FriendId");
                tb.HasCheckConstraint("chk_ordered_ids", "UserId < FriendId");
            });
            
        });
        

        // User games
        modelBuilder.Entity<UserGame>()
            .HasIndex(ug => new { ug.UserId, ug.ExternalGameId, ug.Source })
            .IsUnique();
        
        modelBuilder.Entity<UserGame>()
            .Property(ug => ug.Source)
            .HasConversion<string>();
        
        modelBuilder.Entity<UserGame>()
            .HasOne(ug => ug.User)
            .WithMany()
            .HasForeignKey(ug => ug.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        
        // User settings
        modelBuilder.Entity<UserSettings>()
            .Property(s => s.SettingsJson)
            .HasColumnType("json");
        
        modelBuilder.Entity<UserSettings>()
            .HasOne(p => p.User)
            .WithOne(u => u.Settings)
            .HasForeignKey<UserSettings>(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        

        // Game Collections
        modelBuilder.Entity<CollectionGame>()
            .HasKey(cg => new { cg.CollectionId, cg.ExternalGameId, cg.Source });
        
        modelBuilder.Entity<CollectionGame>()
            .Property(cg => cg.Source)
            .HasConversion<string>();
        
        modelBuilder.Entity<CollectionGame>()
            .HasOne(cg => cg.Collection)
            .WithMany(c => c.CollectionGames)
            .HasForeignKey(cg => cg.CollectionId)
            .OnDelete(DeleteBehavior.Cascade);
        
        // User status
        modelBuilder.Entity<UserStatus>()
            .HasKey(s => s.UserId);
        
        modelBuilder.Entity<UserStatus>()
            .HasOne(p => p.User)
            .WithOne(u => u.Status)
            .HasForeignKey<UserStatus>(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        
        
        // User Activity
        modelBuilder.Entity<UserActivity>()
            .Property(a => a.ActivityType)
            .HasConversion<string>();

        modelBuilder.Entity<UserActivity>()
            .Property(a => a.Source)
            .HasConversion<string>();
        
        modelBuilder.Entity<UserActivity>()
            .HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        
        // Refresh Tokens
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(rt => rt.Id);

            entity.HasIndex(rt => rt.TokenHash).IsUnique();
            entity.HasIndex(rt => rt.UserId);

            entity.Property(rt => rt.TokenHash)
                .IsRequired()
                .HasMaxLength(64);

            entity.HasOne(rt => rt.User)
                .WithMany(u => u.RefreshTokens)
                .HasForeignKey(rt => rt.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}