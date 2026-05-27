namespace MarketFlow.Application.Features.AI.DTOs;

public class AiPurchaseRecommendationDto
{
    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public int CurrentStock { get; set; }

    public long TotalQuantitySold { get; set; }

    public decimal AverageDailySales { get; set; }

    public decimal ForecastDemand { get; set; }

    public int PendingPurchaseQuantity { get; set; }

    public int RecommendedPurchaseQuantity { get; set; }

    public int? PreferredSupplierId { get; set; }

    public string? PreferredSupplierName { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string Explanation { get; set; } = string.Empty;
}
