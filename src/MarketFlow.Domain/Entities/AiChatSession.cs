namespace MarketFlow.Domain.Entities;

public class AiChatSession : TenantEntity
{
    public int UserId { get; set; }

    public int? MarketId { get; set; }

    public string? Title { get; set; }

    public string MessageHistory { get; set; } = "[]";
}
