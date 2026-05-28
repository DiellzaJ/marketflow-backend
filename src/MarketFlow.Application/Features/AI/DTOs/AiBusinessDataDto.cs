namespace MarketFlow.Application.Features.AI.DTOs;

public class AiBusinessDataDto
{
    public AiBusinessDataFilterDto Filters { get; set; } = new();

    public AiSalesMetricsDto SalesMetrics { get; set; } = new();

    public IReadOnlyCollection<AiTopSellingProductDto> TopSellingProducts { get; set; } = [];

    public IReadOnlyCollection<AiLowStockProductDto> LowStockProducts { get; set; } = [];

    public IReadOnlyCollection<AiInventoryMovementMetricDto> InventoryMovementMetrics { get; set; } = [];

    public IReadOnlyCollection<AiSupplierPurchaseMetricDto> SupplierPurchaseMetrics { get; set; } = [];

    public IReadOnlyCollection<AiSalesByMarketDto> SalesByMarket { get; set; } = [];

    public IReadOnlyCollection<AiSalesByCategoryDto> SalesByCategory { get; set; } = [];

    public IReadOnlyCollection<AiDailySalesSummaryDto> DailySalesSummaries { get; set; } = [];
}

public class AiBusinessDataFilterDto
{
    public DateOnly? From { get; set; }

    public DateOnly? To { get; set; }

    public int? MarketId { get; set; }

    public int? DepartmentId { get; set; }
}

public class AiSalesMetricsDto
{
    public decimal TotalRevenue { get; set; }

    public long TotalSales { get; set; }

    public long TotalItemsSold { get; set; }

    public decimal AverageSaleAmount { get; set; }
}

public class AiTopSellingProductDto
{
    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public long QuantitySold { get; set; }

    public decimal Revenue { get; set; }
}

public class AiLowStockProductDto
{
    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public int MarketId { get; set; }

    public string MarketName { get; set; } = string.Empty;

    public int? DepartmentId { get; set; }

    public string? DepartmentName { get; set; }

    public int Quantity { get; set; }

    public int ReservedQuantity { get; set; }

    public int AvailableQuantity { get; set; }

    public int MinimumStockAlert { get; set; }

    public int SuggestedRestockQuantity { get; set; }
}

public class AiInventoryMovementMetricDto
{
    public string MovementType { get; set; } = string.Empty;

    public long MovementCount { get; set; }

    public long TotalQuantityChanged { get; set; }
}

public class AiSupplierPurchaseMetricDto
{
    public int SupplierId { get; set; }

    public string SupplierName { get; set; } = string.Empty;

    public long PurchaseCount { get; set; }

    public decimal TotalPurchaseAmount { get; set; }

    public long TotalPurchasedQuantity { get; set; }
}

public class AiSalesByMarketDto
{
    public int MarketId { get; set; }

    public string MarketName { get; set; } = string.Empty;

    public long SalesCount { get; set; }

    public long ItemsSold { get; set; }

    public decimal Revenue { get; set; }
}

public class AiSalesByCategoryDto
{
    public int? CategoryId { get; set; }

    public string CategoryName { get; set; } = string.Empty;

    public long QuantitySold { get; set; }

    public decimal Revenue { get; set; }
}

public class AiDailySalesSummaryDto
{
    public DateOnly SaleDate { get; set; }

    public long SalesCount { get; set; }

    public long ItemsSold { get; set; }

    public decimal Revenue { get; set; }
}
