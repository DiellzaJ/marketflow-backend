namespace MarketFlow.Application.Features.Products.Configuration;

public static class ProductSortFields
{
    public const string Id = "id";
    public const string Name = "name";
    public const string Barcode = "barcode";
    public const string Category = "category";
    public const string CategoryName = "categoryName";
    public const string UnitPrice = "unitPrice";
    public const string CostPrice = "costPrice";
    public const string TaxRate = "taxRate";
    public const string MinStockAlert = "minStockAlert";
    public const string IsActive = "isActive";

    private static readonly HashSet<string> AllowedFields = new(StringComparer.OrdinalIgnoreCase)
    {
        Id,
        Name,
        Barcode,
        Category,
        CategoryName,
        UnitPrice,
        CostPrice,
        TaxRate,
        MinStockAlert,
        IsActive
    };

    public static bool IsAllowed(string? sortBy)
    {
        return string.IsNullOrWhiteSpace(sortBy) || AllowedFields.Contains(sortBy.Trim());
    }
}
