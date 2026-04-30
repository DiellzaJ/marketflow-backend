namespace MarketFlow.Domain.Entities;

public class Inventory : TenantEntity
{
    public Guid ProductId { get; set; }

    public int QuantityOnHand { get; set; }

    public int ReorderLevel { get; set; }

    public Product? Product { get; set; }
}
