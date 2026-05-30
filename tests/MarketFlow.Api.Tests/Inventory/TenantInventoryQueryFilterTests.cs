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

    [Fact]
    public void InventoryBarcodeCacheVariant_IncludesInventoryScope()
    {
        var query = new InventoryListQuery
        {
            Barcode = "123",
            Page = 1,
            PageSize = 20,
            SortBy = "productName",
            SortDirection = "asc"
        };

        var companyVariant = CreateInventoryBarcodeLookupVariantKey(query, GetInventoryScope("Company"));
        var marketVariant = CreateInventoryBarcodeLookupVariantKey(query, CreateInventoryScope("Market", 1));
        var departmentVariant = CreateInventoryBarcodeLookupVariantKey(query, CreateInventoryScope("Department", 1, 2));

        Assert.NotEqual(companyVariant, marketVariant);
        Assert.NotEqual(marketVariant, departmentVariant);
        Assert.NotEqual(companyVariant, departmentVariant);
    }

    [Fact]
    public void InventoryBarcodeCacheVariant_IncludesPaginationAndSort()
    {
        var scope = GetInventoryScope("Company");
        var firstVariant = CreateInventoryBarcodeLookupVariantKey(new InventoryListQuery
        {
            Barcode = "123",
            Page = 1,
            PageSize = 1,
            SortBy = "productName",
            SortDirection = "asc"
        }, scope);
        var secondVariant = CreateInventoryBarcodeLookupVariantKey(new InventoryListQuery
        {
            Barcode = "123",
            Page = 1,
            PageSize = 20,
            SortBy = "quantity",
            SortDirection = "desc"
        }, scope);

        Assert.NotEqual(firstVariant, secondVariant);
    }

    private static string BuildInventoryWhereClause(InventoryListQuery query)
    {
        var method = typeof(TenantQueryService).GetMethod(
            "BuildInventoryWhereClause",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        return Assert.IsType<string>(method.Invoke(null, [query, GetInventoryScope("Company")]));
    }

    private static string CreateInventoryBarcodeLookupVariantKey(InventoryListQuery query, object scope)
    {
        var method = typeof(TenantQueryService).GetMethod(
            "CreateInventoryBarcodeLookupVariantKey",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        return Assert.IsType<string>(method.Invoke(null, [query, scope]));
    }

    private static object GetInventoryScope(string propertyName)
    {
        var scopeType = GetInventoryScopeType();
        var property = scopeType.GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(property);

        return property.GetValue(null) ?? throw new InvalidOperationException("Inventory scope was not found.");
    }

    private static object CreateInventoryScope(string methodName, params object[] arguments)
    {
        var method = GetInventoryScopeType().GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);

        return method.Invoke(null, arguments) ?? throw new InvalidOperationException("Inventory scope was not created.");
    }

    private static Type GetInventoryScopeType()
    {
        var scopeType = typeof(TenantQueryService).GetNestedType(
            "InventoryScope",
            BindingFlags.NonPublic);
        Assert.NotNull(scopeType);

        return scopeType;
    }
}
