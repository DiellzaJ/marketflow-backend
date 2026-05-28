namespace MarketFlow.Application.Features.AI.DTOs;

public class AiInventoryInsightDataDto
{
    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public int CurrentStock { get; set; }

    public int MinimumStockAlert { get; set; }

    public long TotalQuantitySold { get; set; }
}
