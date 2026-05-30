namespace MarketFlow.Domain.Entities;

public class Product : TenantEntity
{
    public string Name { get; set; } = string.Empty;

    public string Sku { get; set; } = string.Empty;

    public decimal UnitPrice { get; set; }

    public Guid CategoryId { get; set; }

    public Category? Category { get; set; }
}
