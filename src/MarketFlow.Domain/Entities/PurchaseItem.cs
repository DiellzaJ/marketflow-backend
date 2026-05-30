namespace MarketFlow.Domain.Entities;

public class PurchaseItem : TenantEntity
{
    public int PurchaseId { get; set; }

    public Purchase? Purchase { get; set; }

    public int ProductId { get; set; }

    public Product? Product { get; set; }

    public int Quantity { get; set; }

    public int ReceivedQuantity { get; set; }

    public decimal UnitCost { get; set; }
}
