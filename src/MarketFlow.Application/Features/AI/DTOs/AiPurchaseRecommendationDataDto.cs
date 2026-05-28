namespace MarketFlow.Application.Features.AI.DTOs;

public class AiPurchaseRecommendationDataDto
{
    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public int CurrentStock { get; set; }

    public long TotalQuantitySold { get; set; }

    public int PendingPurchaseQuantity { get; set; }

    public int? PreferredSupplierId { get; set; }

    public string? PreferredSupplierName { get; set; }
}
