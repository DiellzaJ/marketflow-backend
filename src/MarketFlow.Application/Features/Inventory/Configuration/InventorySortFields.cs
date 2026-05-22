namespace MarketFlow.Application.Features.Inventory.Configuration;

public static class InventorySortFields
{
    public const string ProductName = "productName";
    public const string Barcode = "barcode";
    public const string MarketName = "marketName";
    public const string DepartmentName = "departmentName";
    public const string Quantity = "quantity";
    public const string AvailableQuantity = "availableQuantity";
    public const string UpdatedAt = "updatedAt";

    private static readonly HashSet<string> AllowedFields = new(StringComparer.OrdinalIgnoreCase)
    {
        ProductName,
        Barcode,
        MarketName,
        DepartmentName,
        Quantity,
        AvailableQuantity,
        UpdatedAt
    };

    public static bool IsAllowed(string? sortBy)
    {
        return string.IsNullOrWhiteSpace(sortBy) || AllowedFields.Contains(sortBy.Trim());
    }
}
