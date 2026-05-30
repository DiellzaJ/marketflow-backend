namespace MarketFlow.Domain.Entities;

public class Company
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string SchemaName { get; set; } = string.Empty;

    public string CompanyType { get; set; } = "SMALL";

    public string SubscriptionPlan { get; set; } = "BASIC";

    public int MaxMarkets { get; set; } = 5;

    public int MaxUsers { get; set; } = 50;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<User> Users { get; set; } = new List<User>();
}