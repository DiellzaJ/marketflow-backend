namespace MarketFlow.Application.Features.Inventory.DTOs;

public class InventoryItemDto
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public string? Barcode { get; set; }

    public int? CategoryId { get; set; }

    public string? CategoryName { get; set; }

    public decimal UnitPrice { get; set; }

    public int MinStockAlert { get; set; }

    public int SuggestedRestockQuantity { get; set; }

    public int MarketId { get; set; }

    public string MarketName { get; set; } = string.Empty;

    public int? DepartmentId { get; set; }

    public string? DepartmentName { get; set; }

    public int Quantity { get; set; }

    public int ReservedQuantity { get; set; }

    public int AvailableQuantity { get; set; }

    public bool IsLowStock { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public int? LastUpdatedBy { get; set; }
}
