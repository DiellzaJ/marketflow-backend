using MarketFlow.Domain.Enums;

namespace MarketFlow.Domain.Entities;

public class Sale : TenantEntity
{
    public Guid CustomerId { get; set; }

    public Customer? Customer { get; set; }

    public string ReferenceNumber { get; set; } = string.Empty;

    public SaleStatus Status { get; set; } = SaleStatus.Draft;

    public decimal TotalAmount { get; set; }

    public ICollection<SaleItem> Items { get; set; } = new List<SaleItem>();
}
