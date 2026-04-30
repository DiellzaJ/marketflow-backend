using MarketFlow.Domain.Entities;
using MarketFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MarketFlow.Infrastructure.Repositories;

public class ProductRepository(ApplicationDbContext dbContext)
{
    public Task<List<Product>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return dbContext.Products.ToListAsync(cancellationToken);
    }
}
