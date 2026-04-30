namespace MarketFlow.Domain.Entities;

public class Supplier : TenantEntity
{
    public string Name { get; set; } = string.Empty;

    public string ContactEmail { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;
}
