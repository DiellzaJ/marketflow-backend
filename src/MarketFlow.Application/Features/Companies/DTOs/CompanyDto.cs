namespace MarketFlow.Application.Features.Companies.DTOs;

public class CompanyDto
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string SchemaName { get; set; } = string.Empty;

    public string CompanyType { get; set; } = string.Empty;

    public string SubscriptionPlan { get; set; } = string.Empty;

    public int MaxMarkets { get; set; }

    public int MaxUsers { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
