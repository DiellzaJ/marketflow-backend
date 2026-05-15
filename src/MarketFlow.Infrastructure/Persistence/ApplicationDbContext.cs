using MarketFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MarketFlow.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // Global schema tables
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<User> Users => Set<User>();

    // Temporary DbSets because existing repositories reference them.
    // These will be configured properly later in the tenant schema issue.
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<Inventory> Inventories => Set<Inventory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("public");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // Important:
        // Do not include tenant tables in InitialGlobalSchema migration yet.
        modelBuilder.Ignore<Product>();
        modelBuilder.Ignore<Sale>();
        modelBuilder.Ignore<Inventory>();
    }
}