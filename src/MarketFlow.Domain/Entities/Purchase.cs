using MarketFlow.Domain.Enums;

namespace MarketFlow.Domain.Entities;

public class Purchase : TenantEntity
{
    public Guid SupplierId { get; set; }

    public Supplier? Supplier { get; set; }

    public string ReferenceNumber { get; set; } = string.Empty;

    public PurchaseStatus Status { get; set; } = PurchaseStatus.Draft;

    public decimal TotalAmount { get; set; }

    public ICollection<PurchaseItem> Items { get; set; } = new List<PurchaseItem>();
}
