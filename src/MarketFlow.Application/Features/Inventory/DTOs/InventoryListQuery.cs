namespace MarketFlow.Application.Features.Inventory.DTOs;

public class InventoryListQuery
{
    public string? Search { get; set; }

    public int? ProductId { get; set; }

    public string? Barcode { get; set; }

    public int? CategoryId { get; set; }

    public int? MarketId { get; set; }

    public int? DepartmentId { get; set; }

    public bool LowStockOnly { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;

    public string? SortBy { get; set; } = "productName";

    public string? SortDirection { get; set; } = "asc";
}
