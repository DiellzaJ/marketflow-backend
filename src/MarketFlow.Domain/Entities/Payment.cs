using MarketFlow.Domain.Enums;

namespace MarketFlow.Domain.Entities;

public class Payment : TenantEntity
{
    public Guid SaleId { get; set; }

    public Sale? Sale { get; set; }

    public decimal Amount { get; set; }

    public PaymentMethod Method { get; set; } = PaymentMethod.Cash;

    public DateTime PaidAt { get; set; } = DateTime.UtcNow;
}
