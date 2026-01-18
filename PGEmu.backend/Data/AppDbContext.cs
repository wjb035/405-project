using Microsoft.EntityFrameworkCore;
using PGEmuBackend.Models;

namespace PGEmuBackend.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options) {}

    public DbSet<User> Users => Set<User>();
}