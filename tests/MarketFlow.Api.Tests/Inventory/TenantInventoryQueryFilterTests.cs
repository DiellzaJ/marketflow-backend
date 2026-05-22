using System.Reflection;
using MarketFlow.Application.Features.Inventory.DTOs;
using MarketFlow.Infrastructure.Persistence;

namespace MarketFlow.Api.Tests.Inventory;

public sealed class TenantInventoryQueryFilterTests
{
    [Fact]
    public void InventoryListFilter_IncludesSearchByProductNameOrBarcode()
    {
        var whereClause = BuildInventoryWhereClause(new InventoryListQuery { Search = "milk" });

        Assert.Contains("p.name ILIKE @search", whereClause, StringComparison.Ordinal);
        Assert.Contains("p.barcode ILIKE @search", whereClause, StringComparison.Ordinal);
    }

    [Fact]
    public void InventoryListFilter_IncludesFrontendFilters()
    {
        var whereClause = BuildInventoryWhereClause(new InventoryListQuery
        {
            ProductId = 1,
            Barcode = "abc",
            CategoryId = 2,
            MarketId = 3,
            DepartmentId = 4,
            LowStockOnly = true
        });

        Assert.Contains("i.product_id = @product_id", whereClause, StringComparison.Ordinal);
        Assert.Contains("p.barcode ILIKE @barcode", whereClause, StringComparison.Ordinal);
        Assert.Contains("p.category_id = @category_id", whereClause, StringComparison.Ordinal);
        Assert.Contains("i.market_id = @market_id", whereClause, StringComparison.Ordinal);
        Assert.Contains("i.department_id = @department_id", whereClause, StringComparison.Ordinal);
        Assert.Contains("i.quantity <= p.min_stock_alert", whereClause, StringComparison.Ordinal);
    }

    private static string BuildInventoryWhereClause(InventoryListQuery query)
    {
        var scopeType = typeof(TenantQueryService).GetNestedType(
            "InventoryScope",
            BindingFlags.NonPublic);
        Assert.NotNull(scopeType);

        var companyProperty = scopeType.GetProperty(
            "Company",
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(companyProperty);

        var method = typeof(TenantQueryService).GetMethod(
            "BuildInventoryWhereClause",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        return Assert.IsType<string>(method.Invoke(null, [query, companyProperty.GetValue(null)]));
    }
}
