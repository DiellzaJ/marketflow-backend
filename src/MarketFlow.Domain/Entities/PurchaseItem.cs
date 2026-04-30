namespace MarketFlow.Domain.Entities;

public class PurchaseItem : TenantEntity
{
    public Guid PurchaseId { get; set; }

    public Purchase? Purchase { get; set; }

    public Guid ProductId { get; set; }

    public Product? Product { get; set; }

    public int Quantity { get; set; }

    public decimal UnitCost { get; set; }
}
