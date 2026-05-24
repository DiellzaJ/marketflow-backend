namespace MarketFlow.Application.Features.Sales.Configuration;

public static class SalesHistorySortFields
{
    public const string ReferenceNumber = "referenceNumber";
    public const string SaleDate = "saleDate";
    public const string MarketName = "marketName";
    public const string CashierName = "cashierName";
    public const string Status = "status";
    public const string PaymentMethod = "paymentMethod";
    public const string TotalAmount = "totalAmount";
    public const string ItemCount = "itemCount";

    private static readonly HashSet<string> AllowedFields = new(StringComparer.OrdinalIgnoreCase)
    {
        ReferenceNumber,
        SaleDate,
        MarketName,
        CashierName,
        Status,
        PaymentMethod,
        TotalAmount,
        ItemCount
    };

    public static bool IsAllowed(string? sortBy)
    {
        return string.IsNullOrWhiteSpace(sortBy) || AllowedFields.Contains(sortBy.Trim());
    }
}
