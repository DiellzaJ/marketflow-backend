namespace MarketFlow.Application.Features.AI.DTOs;

public class AiAnomalyDetectionDataDto
{
    public IReadOnlyCollection<AiAnomalySaleDataDto> Sales { get; set; } = [];

    public IReadOnlyCollection<AiAnomalySaleItemDataDto> SaleItems { get; set; } = [];

    public IReadOnlyCollection<AiAnomalyStockMovementDataDto> StockMovements { get; set; } = [];

    public IReadOnlyCollection<AiAnomalyDailyProductSalesDataDto> DailyProductSales { get; set; } = [];
}

public class AiAnomalySaleDataDto
{
    public int SaleId { get; set; }

    public string ReferenceNumber { get; set; } = string.Empty;

    public DateOnly SaleDate { get; set; }

    public decimal TotalAmount { get; set; }

    public decimal DiscountAmount { get; set; }
}

public class AiAnomalySaleItemDataDto
{
    public int SaleId { get; set; }

    public string ReferenceNumber { get; set; } = string.Empty;

    public DateOnly SaleDate { get; set; }

    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal CostPrice { get; set; }
}

public class AiAnomalyStockMovementDataDto
{
    public int MovementId { get; set; }

    public string MovementType { get; set; } = string.Empty;

    public int QuantityChanged { get; set; }

    public string? ReferenceNumber { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public bool HasMatchingReference { get; set; }
}

public class AiAnomalyDailyProductSalesDataDto
{
    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public DateOnly SaleDate { get; set; }

    public long QuantitySold { get; set; }

    public decimal AverageDailyQuantity { get; set; }
}
