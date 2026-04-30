using MarketFlow.Domain.Entities;
using MarketFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MarketFlow.Infrastructure.Repositories;

public class InventoryRepository(ApplicationDbContext dbContext)
{
    public Task<List<Inventory>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return dbContext.Inventories.ToListAsync(cancellationToken);
    }
}
