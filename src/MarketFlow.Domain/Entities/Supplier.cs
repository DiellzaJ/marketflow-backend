namespace MarketFlow.Domain.Entities;

public class Supplier : TenantEntity
{
    public string Name { get; set; } = string.Empty;

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? Address { get; set; }

    public bool IsActive { get; set; } = true;
}
