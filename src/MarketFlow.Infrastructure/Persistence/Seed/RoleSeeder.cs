using Microsoft.EntityFrameworkCore;

namespace MarketFlow.Infrastructure.Persistence.Seed;

public static class RoleSeeder
{
    public static Task SeedAsync(
        DbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
