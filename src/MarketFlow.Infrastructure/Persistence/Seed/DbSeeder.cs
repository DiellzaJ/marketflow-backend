using Microsoft.EntityFrameworkCore;

namespace MarketFlow.Infrastructure.Persistence.Seed;

public class DbSeeder
{
    public async Task SeedAsync(
        DbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        await RoleSeeder.SeedAsync(dbContext, cancellationToken);
    }
}
