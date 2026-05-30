namespace MarketFlow.Domain.Entities;

public abstract class TenantEntity : BaseEntity
{
    public Guid CompanyId { get; set; }
}
