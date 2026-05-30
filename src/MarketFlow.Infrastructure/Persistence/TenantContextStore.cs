using MarketFlow.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace MarketFlow.Infrastructure.Persistence;

public sealed class TenantContextStore : ITenantContextStore
{
    private readonly ApplicationDbContext _dbContext;

    public TenantContextStore(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<TenantContext?> GetTenantContextAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Users
            .AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => new TenantContext(
                x.Company.SchemaName,
                x.IsActive,
                x.Company.IsActive))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
