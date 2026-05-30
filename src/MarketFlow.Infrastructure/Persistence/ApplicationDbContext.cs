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
    public DbSet<RevokedAccessToken> RevokedAccessTokens => Set<RevokedAccessToken>();

    // Temporary DbSets because existing repositories reference them.
    // These will be configured properly later in the tenant schema issue.
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<Inventory> Inventories => Set<Inventory>();
    public DbSet<AiAnalysisRequest> AiAnalysisRequests => Set<AiAnalysisRequest>();
    public DbSet<AiAnalysisResult> AiAnalysisResults => Set<AiAnalysisResult>();
    public DbSet<AiChatSession> AiChatSessions => Set<AiChatSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("public");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // Tenant tables are provisioned in per-schema migrations, not in public.
        modelBuilder.Ignore<Product>();
        modelBuilder.Ignore<Sale>();
        modelBuilder.Ignore<Inventory>();
        modelBuilder.Ignore<AiAnalysisRequest>();
        modelBuilder.Ignore<AiAnalysisResult>();
        modelBuilder.Ignore<AiChatSession>();
    }
}
