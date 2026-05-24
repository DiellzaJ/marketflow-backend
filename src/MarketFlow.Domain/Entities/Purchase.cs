using MarketFlow.Domain.Enums;

namespace MarketFlow.Domain.Entities;

public class Purchase : TenantEntity
{
    public int SupplierId { get; set; }

    public Supplier? Supplier { get; set; }

    public int MarketId { get; set; }

    public DateOnly PurchaseDate { get; set; }

    public PurchaseStatus Status { get; set; } = PurchaseStatus.Draft;

    public decimal TotalAmount { get; set; }

    public string? Notes { get; set; }

    public ICollection<PurchaseItem> Items { get; set; } = new List<PurchaseItem>();
}
