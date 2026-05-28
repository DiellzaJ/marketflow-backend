namespace MarketFlow.Application.Features.AI.DTOs;

public class AiInventoryRecommendationDto
{
    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public int CurrentStock { get; set; }

    public int MinimumStockAlert { get; set; }

    public long TotalQuantitySold { get; set; }

    public decimal AverageDailySales { get; set; }

    public string IssueType { get; set; } = string.Empty;

    public string UrgencyLevel { get; set; } = string.Empty;

    public int RecommendedPurchaseQuantity { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string Explanation { get; set; } = string.Empty;
}
