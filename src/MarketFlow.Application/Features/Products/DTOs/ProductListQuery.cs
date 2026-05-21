namespace MarketFlow.Application.Features.Products.DTOs;

public class ProductListQuery
{
    public string? Search { get; set; }

    public string? Name { get; set; }

    public string? Barcode { get; set; }

    public int? CategoryId { get; set; }

    public string? Category { get; set; }

    public bool? IsActive { get; set; }

    public bool IncludeInactive { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;

    public string? SortBy { get; set; } = "name";

    public string? SortDirection { get; set; } = "asc";
}
