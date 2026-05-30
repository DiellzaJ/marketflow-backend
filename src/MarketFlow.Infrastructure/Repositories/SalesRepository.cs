using MarketFlow.Domain.Entities;
using MarketFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MarketFlow.Infrastructure.Repositories;

public class SalesRepository(ApplicationDbContext dbContext)
{
    public Task<List<Sale>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return dbContext.Sales.ToListAsync(cancellationToken);
    }
}
