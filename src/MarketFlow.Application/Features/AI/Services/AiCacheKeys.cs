using MarketFlow.Application.Features.AI.DTOs;

namespace MarketFlow.Application.Features.AI.Services;

public static class AiCacheKeys
{
    private const string All = "all";

    public static string Dashboard(
        int companyId,
        AiDashboardSummaryRequest request,
        string provider,
        string model) =>
        string.Join(
            ':',
            "ai",
            "dashboard",
            companyId,
            Segment(request.MarketId),
            Segment(request.DepartmentId),
            Segment(request.From),
            Segment(request.To),
            Math.Clamp(request.TopProductsLimit, 1, 20),
            Math.Clamp(request.LowStockLimit, 1, 25),
            Segment(provider),
            Segment(model));

    public static string Inventory(
        int companyId,
        AiInventoryRecommendationRequest request,
        string provider,
        string model) =>
        string.Join(
            ':',
            "ai",
            "inventory",
            companyId,
            Segment(request.MarketId),
            Segment(request.DepartmentId),
            Math.Clamp(request.SalesHistoryDays, 1, 365),
            Math.Clamp(request.TargetStockDays, 1, 365),
            Math.Clamp(request.OverstockDays, 1, 365),
            Segment(provider),
            Segment(model));

    public static string Inventory(
        int companyId,
        AiPurchaseRecommendationRequest request,
        string provider,
        string model) =>
        string.Join(
            ':',
            "ai",
            "inventory",
            companyId,
            Segment(request.MarketId),
            Segment(request.DepartmentId),
            Math.Clamp(request.SalesHistoryDays, 1, 365),
            Math.Clamp(request.TargetStockDays, 1, 365),
            "purchase",
            Segment(provider),
            Segment(model));

    public static string Supplier(
        int companyId,
        AiSupplierInsightRequest request,
        string provider,
        string model) =>
        string.Join(
            ':',
            "ai",
            "supplier",
            companyId,
            Segment(request.MarketId),
            Segment(request.From),
            Segment(request.To),
            Segment(provider),
            Segment(model));

    public static IReadOnlyCollection<string> CompanyPrefixes(int companyId) =>
    [
        $"ai:dashboard:{companyId}:",
        $"ai:inventory:{companyId}:",
        $"ai:supplier:{companyId}:"
    ];

    private static string Segment(int? value) => value?.ToString() ?? All;

    private static string Segment(DateOnly? value) => value?.ToString("yyyy-MM-dd") ?? All;

    private static string Segment(string value) =>
        Uri.EscapeDataString(string.IsNullOrWhiteSpace(value) ? All : value.Trim());
}
